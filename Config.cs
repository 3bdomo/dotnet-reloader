namespace DotnetReloader;

public class ReloaderConfig
{
    /// <summary>Root directory to watch for file changes (solution root or project folder)</summary>
    public string WatchPath { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>The actual .csproj folder passed to dotnet build/run</summary>
    public string ProjectPath { get; set; } = Directory.GetCurrentDirectory();

    public int DebounceSeconds { get; set; } = 5;
    public string[] WatchExtensions { get; set; } = [".cs", ".csproj", ".json", ".xml", ".razor", ".html", ".css","cshtml"];
    public string[] IgnoreFolders { get; set; } = ["bin", "obj", ".git", ".vs", "node_modules"];
    public string BuildArgs { get; set; } = "";
    public string RunArgs { get; set; } = "";
}

public static class ConfigParser
{
    public static ReloaderConfig Parse(string[] args)
    {
        var config = new ReloaderConfig();
        bool projectExplicitlySet = false;

        if (args.Length > 0 && !args[0].StartsWith("--"))
        {
            config.WatchPath = Path.GetFullPath(args[0]);
            config.ProjectPath = config.WatchPath;
            projectExplicitlySet = true;
        }

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p" when i + 1 < args.Length:
                    config.WatchPath = Path.GetFullPath(args[++i]);
                    config.ProjectPath = config.WatchPath;
                    projectExplicitlySet = true;
                    break;

                case "--delay" or "-d" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var delay))
                        config.DebounceSeconds = delay;
                    break;

                case "--ext" or "-e" when i + 1 < args.Length:
                    config.WatchExtensions = args[++i]
                        .Split(',')
                        .Select(e => e.Trim().StartsWith('.') ? e.Trim() : $".{e.Trim()}")
                        .ToArray();
                    break;

                case "--ignore" or "-i" when i + 1 < args.Length:
                    config.IgnoreFolders = args[++i].Split(',').Select(f => f.Trim()).ToArray();
                    break;

                case "--build-args" when i + 1 < args.Length:
                    config.BuildArgs = args[++i];
                    break;

                case "--run-args" when i + 1 < args.Length:
                    config.RunArgs = args[++i];
                    break;

                case "--help" or "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        if (!projectExplicitlySet)
            ResolveProjectPath(config);

        return config;
    }

    /// <summary>
    /// Sets WatchPath = solution root (for FSW), ProjectPath = .csproj folder (for build/run).
    /// Resolution order:
    ///   1. .csproj in current dir → both paths are the same
    ///   2. .sln found → WatchPath = sln root, ProjectPath = matched .csproj folder
    ///   3. Nothing found → both stay as current directory
    /// </summary>
    private static void ResolveProjectPath(ReloaderConfig config)
    {
        var basePath = config.WatchPath;

        // 1. .csproj directly in current directory — no split needed
        var directCsproj = Directory.GetFiles(basePath, "*.csproj", SearchOption.TopDirectoryOnly);
        if (directCsproj.Length > 0)
        {
            config.ProjectPath = basePath;
            return;
        }

        // 2. Look for a .sln
        var slnFiles = Directory.GetFiles(basePath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length == 0)
            return;

        var slnName = Path.GetFileNameWithoutExtension(slnFiles[0]);

        // Match by folder name: MyApp.sln → .../MyApp/MyApp.csproj
        var matchByFolder = Directory
            .GetFiles(basePath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(f)),
                    slnName,
                    StringComparison.OrdinalIgnoreCase));

        if (matchByFolder != null)
        {
            config.WatchPath = basePath;                              // watch the whole solution
            config.ProjectPath = Path.GetDirectoryName(matchByFolder)!; // run the matched project
            Ui.ResolvedFromSolution(slnName, config.ProjectPath);
            return;
        }

        // Match by .csproj filename: MyApp.sln → .../src/MyApp.csproj
        var matchByFileName = Directory
            .GetFiles(basePath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(f),
                    slnName,
                    StringComparison.OrdinalIgnoreCase));

        if (matchByFileName != null)
        {
            config.WatchPath = basePath;
            config.ProjectPath = Path.GetDirectoryName(matchByFileName)!;
            Ui.ResolvedFromSolution(slnName, config.ProjectPath);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            dotnet-reloader — Auto rebuild & run .NET projects on file change

            USAGE:
              dotnet-reloader [project-path] [options]

            OPTIONS:
              --project,   -p <path>     Project path (default: auto-resolved)
              --delay,     -d <seconds>  Debounce delay in seconds (default: 5)
              --ext,       -e <exts>     Comma-separated file extensions to watch
              --ignore,    -i <folders>  Comma-separated folders to ignore
              --build-args <args>        Extra args passed to dotnet build
              --run-args   <args>        Extra args passed to dotnet run
              --help,      -h            Show this help

            AUTO-RESOLUTION (when no project is specified):
              1. Looks for a .csproj in the current directory
              2. Looks for a .sln → watches solution root, runs matched project
              3. Falls back to current directory

            EXAMPLES:
              dotnet-reloader                          # auto-resolve
              dotnet-reloader ./src/MyApi              # explicit path
              dotnet-reloader --delay 3
              dotnet-reloader --run-args "--environment Development"
            """);
    }
}