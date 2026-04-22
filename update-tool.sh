#!/bin/bash
set -e

echo ""
echo "================================================"
echo "  DotnetReloader Clean Update Script"
echo "================================================"
echo ""

TOOLS_DIR="$HOME/.dotnet/tools"
STORE_DIR="$TOOLS_DIR/.store/dotnetreloader"

# Step 1: Clean shell shims
echo "[1/5] Cleaning leftover shell shims..."
if [ -f "$TOOLS_DIR/dotnet-reloader" ]; then
    rm -f "$TOOLS_DIR/dotnet-reloader" 2>/dev/null || true
    echo "        Removed orphaned dotnet-reloader"
fi

# Step 2: Clean store and cache folders
echo "[2/5] Cleaning tool store and NuGet cache..."
if [ -d "$STORE_DIR" ]; then
    rm -rf "$STORE_DIR" 2>/dev/null || echo "        Warning: Could not fully clean store (locked by process)"
else
    echo "        Store folder not found (already clean)"
fi
# Also clear NuGet local cache
dotnet nuget locals all --clear 2>/dev/null || true

# Step 3: Repack
echo "[3/5] Repacking dotnet-reloader..."
dotnet pack dotnet-reloader.csproj -c Release --nologo -o ./nupkg
if [ $? -ne 0 ]; then
    echo "ERROR: Pack failed. Aborting."
    exit 1
fi

# Step 4: Uninstall (soft fail)
echo "[4/5] Uninstalling existing tool (if present)..."
dotnet tool uninstall --global DotnetReloader 2>/dev/null || echo "        Tool was not installed or already removed"

# Step 5: Reinstall
echo "[5/5] Installing fresh tool from local package..."
dotnet tool install --global --add-source ./nupkg DotnetReloader
if [ $? -ne 0 ]; then
    echo ""
    echo "ERROR: Install failed. Check permissions or try running with sudo."
    exit 1
fi

echo ""
echo "================================================"
echo "  Success! Tool installed."
echo "================================================"
echo ""
echo "Usage:"
echo "  dotnet-reloader                    # auto-resolve project"
echo "  dotnet-reloader ./src/MyApp        # explicit path"
echo "  dotnet-reloader --help             # show all options"
echo ""
