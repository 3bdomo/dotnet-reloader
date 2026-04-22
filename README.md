# dotnet-reloader

A smart file watcher that automatically rebuilds and restarts your .NET app whenever you save code.

`dotnet-reloader` watches your project files, debounces noisy file-system events, runs `dotnet build`, and then restarts your app with `dotnet run --no-build`.

## Features

- Watches project files recursively with configurable extensions.
- Debounces rapid file changes to avoid unnecessary rebuilds.
- Ignores common generated folders (`bin`, `obj`, `.git`, `.vs`, `node_modules`) by default.
- Filters duplicate watcher events and post-build artifact noise.
- Kills the previous app process before starting a fresh one.
- Supports extra arguments for both `dotnet build` and `dotnet run`.

## Requirements

- .NET SDK 8.0 or later
- Windows, Linux, or macOS shell/terminal

## Quick Start

Run directly from source:

```powershell
dotnet run --project "./dotnet-reloader.csproj" -- "./"
```

Build once:

```powershell
dotnet build "./dotnet-reloader.csproj" -c Debug
```

## Usage

```text
dotnet-reloader [project-path] [options]
```

### Options

- `--project`, `-p <path>`: Project path (default: current directory)
- `--delay`, `-d <seconds>`: Debounce delay in seconds (default: `5`)
- `--ext`, `-e <exts>`: Comma-separated file extensions
  - Default: `.cs,.csproj,.json,.xml,.razor,.html,.css`
- `--ignore`, `-i <folders>`: Comma-separated folders to ignore
  - Default: `bin,obj,.git,.vs,node_modules`
- `--build-args <args>`: Extra args passed to `dotnet build`
- `--run-args <args>`: Extra args passed to `dotnet run`
- `--help`, `-h`: Show help

### Examples

Watch current directory:

```powershell
dotnet run --project "./dotnet-reloader.csproj" --
```

Watch a specific app with faster debounce:

```powershell
dotnet run --project "./dotnet-reloader.csproj" -- "./src/MyApi" --delay 2
```

Watch only selected extensions and pass runtime args:

```powershell
dotnet run --project "./dotnet-reloader.csproj" -- "./src/MyApi" --ext .cs,.razor,.json --run-args "--environment Development"
```

## Install As a .NET Tool (Optional)

This project is configured with `PackAsTool=true` and command name `dotnet-reloader`.

```powershell
dotnet pack "./dotnet-reloader.csproj" -c Release -o ./nupkg
dotnet tool install --global DotnetReloader --add-source ./nupkg
```

After installation:

```powershell
dotnet-reloader --help
```

## How It Works

1. Starts by building the target project.
2. If build succeeds, launches `dotnet run --no-build` for fast restarts.
3. Watches for matching file changes.
4. Waits for the debounce window to collect burst changes.
5. Rebuilds and restarts the app process.
6. Suppresses duplicate and post-build events to avoid reload loops.

## Troubleshooting

- If nothing happens on save, confirm file extension is included in `--ext`.
- If reload loops occur, ensure generated folders are in `--ignore`.
- If app does not stop cleanly, check for child processes spawned outside the process tree.
- If your build requires custom settings, pass them via `--build-args`.


