#!/bin/bash
#
# BotFarm Release Build Script
#
# Usage:
#   ./build-release.sh                    # Build with version from Directory.Build.props
#   ./build-release.sh -v 0.2.0           # Build with specific version
#   ./build-release.sh --skip-tests       # Skip running tests
#   ./build-release.sh --skip-ui          # Skip building the web UI
#   ./build-release.sh --clean            # Clean build outputs before building
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Default values
VERSION=""
SKIP_TESTS=false
SKIP_UI=false
CLEAN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --skip-ui)
            SKIP_UI=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        -h|--help)
            echo "BotFarm Release Build Script"
            echo ""
            echo "Usage: ./build-release.sh [options]"
            echo ""
            echo "Options:"
            echo "  -v, --version VERSION   Set version number (default: from Directory.Build.props)"
            echo "  --skip-tests            Skip running tests"
            echo "  --skip-ui               Skip building the web UI"
            echo "  --clean                 Clean build outputs before building"
            echo "  -h, --help              Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

echo "========================================"
echo "  BotFarm Release Build Script"
echo "========================================"
echo ""

# Determine version from Directory.Build.props if not specified
if [ -z "$VERSION" ]; then
    VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$SCRIPT_DIR/Directory.Build.props")
fi
echo -e "\033[32mBuilding version: $VERSION\033[0m"

# Update Directory.Build.props with version
sed -i "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|g" "$SCRIPT_DIR/Directory.Build.props"
sed -i "s|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>$VERSION</AssemblyVersion>|g" "$SCRIPT_DIR/Directory.Build.props"
sed -i "s|<FileVersion>[^<]*</FileVersion>|<FileVersion>$VERSION</FileVersion>|g" "$SCRIPT_DIR/Directory.Build.props"
sed -i "s|<InformationalVersion>[^<]*</InformationalVersion>|<InformationalVersion>$VERSION</InformationalVersion>|g" "$SCRIPT_DIR/Directory.Build.props"

RELEASE_DIR="$SCRIPT_DIR/release/BotFarm-$VERSION"

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo -e "\n\033[33mCleaning previous builds...\033[0m"
    rm -rf "$SCRIPT_DIR/release"
    dotnet clean "$SCRIPT_DIR/BotFarm.sln" -c Release -p:Platform=x64 > /dev/null 2>&1 || true
fi

# Build .NET solution
echo -e "\n\033[33m[1/4] Building .NET solution...\033[0m"
if ! dotnet build "$SCRIPT_DIR/BotFarm.sln" -c Release -p:Platform=x64; then
    echo -e "\033[31mBuild failed!\033[0m"
    exit 1
fi
echo -e "\033[32mBuild succeeded!\033[0m"

# Run tests
if [ "$SKIP_TESTS" = false ]; then
    echo -e "\n\033[33m[2/4] Running tests...\033[0m"
    if ! dotnet test "$SCRIPT_DIR/TrinityCore UnitTests/TrinityCore UnitTests.csproj" -c Release --no-build; then
        echo -e "\033[31mTests failed!\033[0m"
        exit 1
    fi
    echo -e "\033[32mTests passed!\033[0m"
else
    echo -e "\n\033[90m[2/4] Skipping tests...\033[0m"
fi

# Build UI
if [ "$SKIP_UI" = false ]; then
    echo -e "\n\033[33m[3/4] Building web UI...\033[0m"
    pushd "$SCRIPT_DIR/botfarm-ui" > /dev/null
    if ! npm install; then
        echo -e "\033[31mnpm install failed!\033[0m"
        exit 1
    fi
    if ! npm run build; then
        echo -e "\033[31mnpm build failed!\033[0m"
        exit 1
    fi
    popd > /dev/null
    echo -e "\033[32mUI build succeeded!\033[0m"
else
    echo -e "\n\033[90m[3/4] Skipping UI build...\033[0m"
fi

# Create release package
echo -e "\n\033[33m[4/4] Creating release package...\033[0m"

# Create release directory structure
mkdir -p "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR/botfarm-ui"

# Copy .NET output
echo "  Copying .NET output..."
cp -r "$SCRIPT_DIR/BotFarm/bin/x64/Release/net8.0/"* "$RELEASE_DIR/"

# Copy UI output (preserve .output directory structure for UILauncher)
if [ "$SKIP_UI" = false ] && [ -d "$SCRIPT_DIR/botfarm-ui/.output" ]; then
    echo "  Copying UI output..."
    mkdir -p "$RELEASE_DIR/botfarm-ui/.output"
    cp -r "$SCRIPT_DIR/botfarm-ui/.output/"* "$RELEASE_DIR/botfarm-ui/.output/"
fi

# Copy native libs
echo "  Copying native libraries..."
LIB_FILES=("cli.dll" "Ijwhost.dll" "cli.runtimeconfig.json" "cli.deps.json")
for file in "${LIB_FILES[@]}"; do
    SOURCE_PATH="$SCRIPT_DIR/BotFarm/lib/$file"
    if [ -f "$SOURCE_PATH" ]; then
        cp "$SOURCE_PATH" "$RELEASE_DIR/"
    else
        echo -e "    \033[33mWarning: $file not found in BotFarm/lib/\033[0m"
    fi
done

# Create release README
echo "  Creating README..."
cat > "$RELEASE_DIR/README.txt" << EOF
BotFarm v$VERSION
==================

A bot farm application for spawning automated World of Warcraft players.

REQUIREMENTS
------------
- Windows x64
- .NET 8.0 Runtime (https://dotnet.microsoft.com/download/dotnet/8.0)
- Node.js 20+ (https://nodejs.org/)
- TrinityCore server with Remote Access enabled
- Map data files (mmaps, vmaps, maps, dbc)

NATIVE LIBRARIES
----------------
The bundled cli.dll is built for tswow's TrinityCore fork. If you're using a
different TrinityCore version and experience pathfinding issues, rebuild cli.dll
from your TrinityCore source with the CLI branch:
https://github.com/jackpoz/TrinityCore/tree/CLI

QUICK START
-----------
1. Extract this archive
2. Edit BotFarm.dll.config with your server settings and data paths
3. Run: BotFarm.exe
4. Web UI opens automatically at http://localhost:3000

For full documentation, visit:
https://github.com/Mathih13/BotFarm
EOF

# Create zip archive
echo "  Creating zip archive..."
ZIP_PATH="$SCRIPT_DIR/release/BotFarm-$VERSION-win-x64.zip"
rm -f "$ZIP_PATH"
pushd "$SCRIPT_DIR/release" > /dev/null
zip -r "BotFarm-$VERSION-win-x64.zip" "BotFarm-$VERSION"
popd > /dev/null

echo ""
echo "========================================"
echo -e "  \033[32mRelease build complete!\033[0m"
echo "========================================"
echo ""
echo "Output:"
echo "  Folder: $RELEASE_DIR"
echo "  Zip:    $ZIP_PATH"
echo ""
