#!/bin/bash
set -euo pipefail

PROJECT_ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
BUILD_DIR="$PROJECT_ROOT/build/macos-server"
SERVER_EXE="$BUILD_DIR/ProjectCNetworkServer"

if ! command -v cmake >/dev/null 2>&1; then
    echo "CMake was not found. Install CMake and add it to PATH."
    exit 1
fi

cmake -S "$PROJECT_ROOT/ProjectC.Server" -B "$BUILD_DIR"
cmake --build "$BUILD_DIR" --config Debug --target ProjectCNetworkServer --parallel

echo "Starting ProjectC with the HyakkiYagyo personal-deck catalog. Press Ctrl+C to stop."
exec "$SERVER_EXE" --port 7777 --data "$PROJECT_ROOT/ProjectC.Server/Data/HyakkiYagyo" "$@"
