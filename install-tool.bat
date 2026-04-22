@echo off
setlocal enabledelayedexpansion

echo.
echo ================================================
echo  DotnetReloader Clean Update Script
echo ================================================
echo.

REM Step 1: Clean shell shims (orphaned from failed uninstalls)
echo [1/5] Cleaning leftover shell shims...
set TOOLS_DIR=%USERPROFILE%\.dotnet\tools
if exist "%TOOLS_DIR%\dotnet-reloader.exe" (
    del /f /q "%TOOLS_DIR%\dotnet-reloader.exe" >nul 2>&1
    echo        Removed orphaned dotnet-reloader.exe
)

REM Step 2: Clean store and cache folders
echo [2/5] Cleaning tool store and NuGet cache...
set STORE_DIR=%USERPROFILE%\.dotnet\tools\.store\dotnetreloader
if exist "%STORE_DIR%" (
    rmdir /s /q "%STORE_DIR%" >nul 2>&1
    if !errorlevel! neq 0 (
        echo        Warning: Could not fully clean store ^(locked by antivirus or process^)
    ) else (
        echo        Cleaned tool store
    )
)
REM Also clear NuGet local cache to avoid stale project.assets.json
nuget locals all -clear >nul 2>&1 || echo.
dotnet nuget locals all --clear >nul 2>&1 || echo.

REM Step 3: Repack
echo [3/5] Repacking dotnet-reloader...
dotnet pack dotnet-reloader.csproj -c Release --nologo -o ./nupkg
if !errorlevel! neq 0 (
    echo ERROR: Pack failed. Aborting.
    pause
    exit /b 1
)

REM Step 4: Uninstall (soft fail)
echo [4/5] Uninstalling existing tool ^(if present^)...
dotnet tool uninstall --global DotnetReloader >nul 2>&1
if !errorlevel! neq 0 (
    echo        Tool was not installed or already removed
)

REM Step 5: Reinstall
echo [5/5] Installing fresh tool from local package...
dotnet tool install --global --add-source ./nupkg DotnetReloader
if !errorlevel! neq 0 (
    echo.
    echo ERROR: Install failed. Try running this script as Administrator.
    pause
    exit /b 1
)

echo.
echo ================================================
echo  Success! Tool installed.
echo ================================================
echo.
echo Usage:
echo   dotnet-reloader                    ^# auto-resolve project
echo   dotnet-reloader ./src/MyApp        ^# explicit path
echo   dotnet-reloader --help             ^# show all options
echo.

pause