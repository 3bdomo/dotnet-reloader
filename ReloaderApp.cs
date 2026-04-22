using System.Diagnostics;

namespace DotnetReloader;

public class ReloaderApp
{
    private readonly ReloaderConfig _config;
    private Process? _runningProcess;
    private CancellationTokenSource? _debounceTokenSource;
    private readonly object _lock = new();
    private bool _isBuilding = false;

    private DateTime _lastBuildCompletedAt = DateTime.MinValue;
    private static readonly TimeSpan PostBuildGrace = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, DateTime> _lastEventTime = new();
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(300);

    private string[] _ignoredPaths = [];

    public ReloaderApp(string[] args)
    {
        _config = ConfigParser.Parse(args);
    }

    public async Task RunAsync()
    {
        Console.CancelKeyPress += OnCancelKeyPress;

        Ui.PrintBanner();
        Ui.PrintConfig(_config);

        if (!Directory.Exists(_config.WatchPath))
        {
            Ui.Error($"Path not found: {_config.WatchPath}");
            Environment.Exit(1);
        }

        if (!Directory.Exists(_config.ProjectPath))
        {
            Ui.Error($"Project not found: {_config.ProjectPath}");
            Environment.Exit(1);
        }

        // Ignore folders are relative to WatchPath
        _ignoredPaths = _config.IgnoreFolders
            .Select(f => Path.GetFullPath(Path.Combine(_config.WatchPath, f)))
            .ToArray();

        await BuildAndRunAsync();

        using var watcher = CreateWatcher();
        Ui.WatchingMessage(_config.WatchPath);

        await Task.Delay(Timeout.Infinite);
    }

    private FileSystemWatcher CreateWatcher()
    {
        // Watch the solution root so changes anywhere in the solution are detected
        var watcher = new FileSystemWatcher(_config.WatchPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
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

    private bool IsPostBuildNoise(string path)
    {
        if (_lastBuildCompletedAt == DateTime.MinValue) return false;

        var cutoff = _lastBuildCompletedAt.Add(PostBuildGrace);
        if (DateTime.UtcNow < cutoff) return true;

        try
        {
            var fileWriteTime = File.GetLastWriteTimeUtc(path);
            if (fileWriteTime <= _lastBuildCompletedAt.Add(TimeSpan.FromMilliseconds(500)))
                return true;
        }
        catch { }

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

            var relativePath = Path.GetRelativePath(_config.WatchPath, filePath);
            Ui.FileChanged(relativePath, changeType, _config.DebounceSeconds);

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

            // Build uses ProjectPath (the .csproj folder)
            var buildSuccess = await RunDotnetCommandAsync("build", _config.ProjectPath, _config.BuildArgs);

            _lastBuildCompletedAt = DateTime.UtcNow;

            if (!buildSuccess)
            {
                Ui.BuildFailed();
                return;
            }

            Ui.BuildSuccess();
            await StartRunProcessAsync();
        }
        finally
        {
            lock (_lock) _isBuilding = false;
            Ui.WatchingMessage(_config.WatchPath);
        }
    }

    private async Task<bool> RunDotnetCommandAsync(string command, string targetPath, string extraArgs)
    {
        Ui.BuildStarted(command);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{command} \"{targetPath}\" {extraArgs}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Ui.BuildOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Ui.BuildError(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private async Task StartRunProcessAsync()
    {
        Ui.RunStarted(_config.ProjectPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            // Run uses ProjectPath (the .csproj folder), not WatchPath
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
            Ui.KillingProcess(_runningProcess.Id);
            _runningProcess.Kill(entireProcessTree: true);
            _runningProcess.WaitForExit(3000);
            _runningProcess.Dispose();
            _runningProcess = null;
        }
        catch (Exception ex)
        {
            Ui.Warning($"Could not kill process: {ex.Message}");
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Ui.Shutdown();
        KillRunningProcess();
        Environment.Exit(0);
    }
}