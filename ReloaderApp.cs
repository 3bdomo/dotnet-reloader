using System.Diagnostics;

namespace DotnetReloader;

public class ReloaderApp
{
    private readonly ReloaderConfig _config;
    private Process? _runningProcess;
    private CancellationTokenSource? _debounceTokenSource;
    private readonly object _lock = new();
    private bool _isBuilding = false;

    // KEY FIX: track when the last build completed.
    // Any FSW event whose file was last modified BEFORE this timestamp is a build artifact — ignore it.
    private DateTime _lastBuildCompletedAt = DateTime.MinValue;
    private static readonly TimeSpan PostBuildGrace = TimeSpan.FromSeconds(2);

    // Dedup: FSW fires 2-3x per save for the same file — suppress within this window
    private readonly Dictionary<string, DateTime> _lastEventTime = new();
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(300);

    // Resolved absolute ignored paths
    private string[] _ignoredPaths = [];

    public ReloaderApp(string[] args)
    {
        _config = ConfigParser.Parse(args);
    }

    public async Task RunAsync()
    {
        Console.CancelKeyPress += OnCancelKeyPress;

        UI.PrintBanner();
        UI.PrintConfig(_config);

        if (!Directory.Exists(_config.ProjectPath))
        {
            UI.Error($"Project path not found: {_config.ProjectPath}");
            Environment.Exit(1);
        }

        _ignoredPaths = _config.IgnoreFolders
            .Select(f => Path.GetFullPath(Path.Combine(_config.ProjectPath, f)))
            .ToArray();

        await BuildAndRunAsync();

        using var watcher = CreateWatcher();
        UI.WatchingMessage(_config.ProjectPath);

        await Task.Delay(Timeout.Infinite);
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_config.ProjectPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            // Increase buffer to avoid missing events on large projects
            InternalBufferSize = 65536
        };

        foreach (var ext in _config.WatchExtensions)
            watcher.Filters.Add($"*{ext}");

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileRenamed;

        return watcher;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        if (IsPostBuildNoise(e.FullPath)) return;
        if (IsDuplicateEvent(e.FullPath)) return;
        TriggerDebounce(e.FullPath, e.ChangeType.ToString());
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        if (IsPostBuildNoise(e.FullPath)) return;
        if (IsDuplicateEvent(e.FullPath)) return;
        TriggerDebounce(e.FullPath, "Renamed");
    }

    /// <summary>
    /// Rejects events on files whose LastWriteTime predates or falls within
    /// the post-build grace window. These are artifacts written by dotnet build,
    /// not by the developer. This is the primary loop-breaker.
    /// </summary>
    private bool IsPostBuildNoise(string path)
    {
        // If no build has run yet, nothing to filter
        if (_lastBuildCompletedAt == DateTime.MinValue) return false;

        var cutoff = _lastBuildCompletedAt.Add(PostBuildGrace);

        // Event arrived within the grace window after build finished → noise
        if (DateTime.UtcNow < cutoff) return true;

        // Event arrived after grace window, but check the file's own write time
        try
        {
            var fileWriteTime = File.GetLastWriteTimeUtc(path);
            // File was written before or during the build → it's a build artifact
            if (fileWriteTime <= _lastBuildCompletedAt.Add(TimeSpan.FromMilliseconds(500)))
                return true;
        }
        catch
        {
            // File deleted or inaccessible — not our concern
        }

        return false;
    }

    private bool ShouldIgnore(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _ignoredPaths.Any(ignored =>
            fullPath.StartsWith(ignored + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            fullPath.Equals(ignored, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDuplicateEvent(string path)
    {
        lock (_lastEventTime)
        {
            var now = DateTime.UtcNow;
            if (_lastEventTime.TryGetValue(path, out var last) && now - last < DuplicateWindow)
                return true;

            _lastEventTime[path] = now;

            if (_lastEventTime.Count > 200)
            {
                var stale = _lastEventTime
                    .Where(kv => now - kv.Value > TimeSpan.FromSeconds(10))
                    .Select(kv => kv.Key).ToList();
                foreach (var k in stale) _lastEventTime.Remove(k);
            }

            return false;
        }
    }

    private void TriggerDebounce(string filePath, string changeType)
    {
        lock (_lock)
        {
            if (_isBuilding) return;

            _debounceTokenSource?.Cancel();
            _debounceTokenSource = new CancellationTokenSource();
            var token = _debounceTokenSource.Token;

            var relativePath = Path.GetRelativePath(_config.ProjectPath, filePath);
            UI.FileChanged(relativePath, changeType, _config.DebounceSeconds);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_config.DebounceSeconds * 1000, token);
                    if (!token.IsCancellationRequested)
                        await BuildAndRunAsync();
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    private async Task BuildAndRunAsync()
    {
        lock (_lock)
        {
            if (_isBuilding) return;
            _isBuilding = true;
        }

        try
        {
            KillRunningProcess();

            var buildSuccess = await RunDotnetCommandAsync("build", _config.BuildArgs);

            // Stamp completion time BEFORE re-enabling events
            _lastBuildCompletedAt = DateTime.UtcNow;

            if (!buildSuccess)
            {
                UI.BuildFailed();
                return;
            }

            UI.BuildSuccess();
            await StartRunProcessAsync();
        }
        finally
        {
            lock (_lock) _isBuilding = false;
            UI.WatchingMessage(_config.ProjectPath);
        }
    }

    private async Task<bool> RunDotnetCommandAsync(string command, string extraArgs)
    {
        UI.BuildStarted(command);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{command} \"{_config.ProjectPath}\" {extraArgs}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) UI.BuildOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) UI.BuildError(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private async Task StartRunProcessAsync()
    {
        UI.RunStarted(_config.ProjectPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_config.ProjectPath}\" --no-build {_config.RunArgs}",
            UseShellExecute = false
        };

        _runningProcess = new Process { StartInfo = psi };
        _runningProcess.Start();

        await Task.CompletedTask;
    }

    private void KillRunningProcess()
    {
        if (_runningProcess == null || _runningProcess.HasExited) return;

        try
        {
            UI.KillingProcess(_runningProcess.Id);
            _runningProcess.Kill(entireProcessTree: true);
            _runningProcess.WaitForExit(3000);
            _runningProcess.Dispose();
            _runningProcess = null;
        }
        catch (Exception ex)
        {
            UI.Warning($"Could not kill process: {ex.Message}");
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        UI.Shutdown();
        KillRunningProcess();
        Environment.Exit(0);
    }
}