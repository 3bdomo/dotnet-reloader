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

        if (args.Length == 0) return config;

        // First positional arg = project path
        if (!args[0].StartsWith("--"))
            config.ProjectPath = Path.GetFullPath(args[0]);

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p" when i + 1 < args.Length:
                    config.ProjectPath = Path.GetFullPath(args[++i]);
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

        return config;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            dotnet-reloader — Auto rebuild & run .NET projects on file change

            USAGE:
              dotnet-reloader [project-path] [options]

            OPTIONS:
              --project,   -p <path>     Project path (default: current directory)
              --delay,     -d <seconds>  Debounce delay in seconds (default: 5)
              --ext,       -e <exts>     Comma-separated file extensions (default: .cs,.csproj,.json)
              --ignore,    -i <folders>  Comma-separated folders to ignore (default: bin,obj,.git)
              --build-args <args>        Extra args passed to dotnet build
              --run-args   <args>        Extra args passed to dotnet run
              --help,      -h            Show this help

            EXAMPLES:
              dotnet-reloader
              dotnet-reloader ./src/MyApi
              dotnet-reloader ./src/MyApi --delay 3
              dotnet-reloader ./src/MyApi --ext .cs,.razor --run-args "--environment Development"
            """);
    }
}
