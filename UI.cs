namespace DotnetReloader;

public static class Ui
{
    private static readonly string Separator = new string('─', 60);

    public static void ResolvedFromSolution(string slnName, string csprojPath)
    {
        SetColor(ConsoleColor.DarkGray);
        Console.WriteLine($"  ↳ No project specified. Found solution '{slnName}.sln'");
        Console.WriteLine($"    Resolved project: {csprojPath}");
        Reset();
    }

    public static void PrintBanner()
    {
        Console.Clear();
        SetColor(ConsoleColor.Cyan);
        Console.WriteLine("""
            ╔══════════════════════════════════════════════════════════╗
            ║          dotnet-reloader  ⚡  Auto Build & Run           ║
            ╚══════════════════════════════════════════════════════════╝
            """);
        Reset();
    }

    public static void PrintConfig(ReloaderConfig config)
    {
        SetColor(ConsoleColor.DarkGray);
        Console.WriteLine($"  Watch    : {config.WatchPath}");
        if (!string.Equals(config.WatchPath, config.ProjectPath, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"  Project  : {config.ProjectPath}");
        Console.WriteLine($"  Delay    : {config.DebounceSeconds}s");
        Console.WriteLine($"  Ext      : {string.Join(", ", config.WatchExtensions)}");
        Console.WriteLine($"  Ignore   : {string.Join(", ", config.IgnoreFolders)}");
        Console.WriteLine(Separator);
        Reset();
    }

    public static void WatchingMessage(string path)
    {
        Console.WriteLine();
        SetColor(ConsoleColor.Green);
        Console.WriteLine($"  👁  Watching for changes... (Ctrl+C to stop)");
        Reset();
        Console.WriteLine();
    }

    public static void FileChanged(string file, string changeType, int debounceSeconds)
    {
        Console.WriteLine();
        Console.Write($"  [{Timestamp()}] ");
        SetColor(ConsoleColor.Yellow);
        Console.Write($"⚡ {changeType}: ");
        SetColor(ConsoleColor.White);
        Console.WriteLine(file);
        SetColor(ConsoleColor.DarkGray);
        Console.WriteLine($"     Waiting {debounceSeconds}s for more changes...");
        Reset();
    }

    public static void BuildStarted(string command)
    {
        Console.WriteLine();
        SetColor(ConsoleColor.Cyan);
        Console.WriteLine($"  [{Timestamp()}] 🔨 Running: dotnet {command}");
        Console.WriteLine(Separator);
        Reset();
    }

    public static void BuildOutput(string line)
    {
        // Colorize warnings and errors inline
        if (line.Contains(": error "))
        {
            SetColor(ConsoleColor.Red);
            Console.WriteLine($"  {line}");
            Reset();
        }
        else if (line.Contains(": warning "))
        {
            SetColor(ConsoleColor.Yellow);
            Console.WriteLine($"  {line}");
            Reset();
        }
        else
        {
            SetColor(ConsoleColor.DarkGray);
            Console.WriteLine($"  {line}");
            Reset();
        }
    }

    public static void BuildError(string line)
    {
        SetColor(ConsoleColor.Red);
        Console.WriteLine($"  {line}");
        Reset();
    }

    public static void BuildSuccess()
    {
        Console.WriteLine(Separator);
        SetColor(ConsoleColor.Green);
        Console.WriteLine($"  [{Timestamp()}] ✅ Build succeeded");
        Reset();
    }

    public static void BuildFailed()
    {
        Console.WriteLine(Separator);
        SetColor(ConsoleColor.Red);
        Console.WriteLine($"  [{Timestamp()}] ❌ Build failed — fix errors and save to retry");
        Reset();
        Console.WriteLine();
        SetColor(ConsoleColor.Green);
        Console.WriteLine($"  👁  Watching for changes...");
        Reset();
    }

    public static void RunStarted(string project)
    {
        SetColor(ConsoleColor.Magenta);
        Console.WriteLine($"  [{Timestamp()}] 🚀 Starting application...");
        Console.WriteLine(Separator);
        Reset();
    }

    public static void KillingProcess(int pid)
    {
        SetColor(ConsoleColor.DarkYellow);
        Console.WriteLine($"\n  [{Timestamp()}] 🛑 Stopping process (PID {pid})...");
        Reset();
    }

    public static void Warning(string message)
    {
        SetColor(ConsoleColor.Yellow);
        Console.WriteLine($"  ⚠  {message}");
        Reset();
    }

    public static void Error(string message)
    {
        SetColor(ConsoleColor.Red);
        Console.WriteLine($"  ✖  {message}");
        Reset();
    }

    public static void Shutdown()
    {
        Console.WriteLine();
        SetColor(ConsoleColor.DarkGray);
        Console.WriteLine("  Shutting down...");
        Reset();
    }

    private static void SetColor(ConsoleColor color) => Console.ForegroundColor = color;
    private static void Reset() => Console.ResetColor();
    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");
}