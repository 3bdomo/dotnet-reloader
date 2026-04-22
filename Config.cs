namespace DotnetReloader;

public class ReloaderConfig
{
    public string ProjectPath { get; set; } = Directory.GetCurrentDirectory();
    public int DebounceSeconds { get; set; } = 5;
    public string[] WatchExtensions { get; set; } = [".cs", ".csproj", ".json", ".xml", ".razor", ".html", ".css"];
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

        // First positional arg = project path
        if (args.Length > 0 && !args[0].StartsWith("--"))
        {
            config.ProjectPath = Path.GetFullPath(args[0]);
            projectExplicitlySet = true;
        }

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p" when i + 1 < args.Length:
                    config.ProjectPath = Path.GetFullPath(args[++i]);
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

        // If no project was explicitly given, try to resolve it automatically
        if (!projectExplicitlySet)
            config.ProjectPath = ResolveProjectPath(config.ProjectPath);

        return config;
    }

    /// <summary>
    /// Resolution order:
    ///   1. Current directory has a .csproj → use it directly
    ///   2. Current directory has a .sln → find a subfolder whose name matches the solution name
    ///   3. Fallback: find a .csproj whose filename matches the solution name
    ///   4. Nothing found → return base path (will show a clear error at startup)
    /// </summary>
    private static string ResolveProjectPath(string basePath)
    {
        // 1. .csproj directly in current directory
        var directCsproj = Directory.GetFiles(basePath, "*.csproj", SearchOption.TopDirectoryOnly);
        if (directCsproj.Length > 0)
            return basePath;

        // 2. Look for a .sln
        var slnFiles = Directory.GetFiles(basePath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length == 0)
            return basePath;

        var slnName = Path.GetFileNameWithoutExtension(slnFiles[0]);

        // Match by folder name — most common convention: MyApp.sln → /MyApp/MyApp.csproj
        var matchByFolder = Directory
            .GetFiles(basePath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(f)),
                    slnName,
                    StringComparison.OrdinalIgnoreCase));

        if (matchByFolder != null)
        {
            Ui.ResolvedFromSolution(slnName, matchByFolder);
            return Path.GetDirectoryName(matchByFolder)!;
        }

        // Match by .csproj filename — e.g. MyApp.sln → /src/MyApp.csproj
        var matchByFileName = Directory
            .GetFiles(basePath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(f),
                    slnName,
                    StringComparison.OrdinalIgnoreCase));

        if (matchByFileName != null)
        {
            Ui.ResolvedFromSolution(slnName, matchByFileName);
            return Path.GetDirectoryName(matchByFileName)!;
        }

        return basePath;
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
              2. Looks for a .sln and finds a project matching the solution name
              3. Falls back to the current directory

            EXAMPLES:
              dotnet-reloader                          # auto-resolve
              dotnet-reloader ./src/MyApi              # explicit path
              dotnet-reloader --delay 3
              dotnet-reloader --run-args "--environment Development"
            """);
    }
}