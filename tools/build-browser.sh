#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
base="${VITE_BASE_PATH:-/AnotherFSM/}"
case "$base" in /*/) ;; *) echo "VITE_BASE_PATH must start and end with /" >&2; exit 1;; esac
dotnet publish demos/FSMDemo.Browser/FSMDemo.Browser.csproj -c Release -o release/browser --nologo -p:NuGetAudit=false
npm_shell=()
if [[ "${OSTYPE:-}" == msys* ]]; then npm_shell=(--script-shell='C:/Program Files/Git/bin/bash.exe'); fi
MSYS_NO_PATHCONV=1 VITE_RUNTIME_MODE=wasm VITE_BASE_PATH="$base" npm "${npm_shell[@]}" run build --prefix demos/FSMDemo.Frontend -- --outDir ../../release/wwwroot --emptyOutDir
cp -R release/browser/wwwroot/. release/wwwroot/
cp release/wwwroot/index.html release/wwwroot/404.html
touch release/wwwroot/.nojekyll
echo "React + .NET WASM published to release/wwwroot (base: $base)"
