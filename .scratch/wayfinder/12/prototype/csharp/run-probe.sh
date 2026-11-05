#!/usr/bin/env bash
# Throwaway runner. Run from any directory using bash path/to/run-probe.sh.
set -u
cd "$(dirname "$0")" || exit 2
root="$(pwd -W 2>/dev/null || pwd)"
export DOTNET_CLI_HOME="$root/.dotnet"
export NUGET_PACKAGES="$root/.packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
mkdir -p logs
: > logs/exit-codes.log
record() { printf '%s => exit %s\n' "$1" "$2" >> logs/exit-codes.log; }
dotnet --version > logs/sdk.log 2>&1
sdk=$?; record 'dotnet --version' "$sdk"
if [ "$sdk" -ne 0 ]; then exit "$sdk"; fi
dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_BINDING > logs/bad-binding-build.log 2>&1
bad=$?; record 'dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_BINDING' "$bad"
dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_REPLACEMENT > logs/bad-replacement-build.log 2>&1
replacement=$?; record 'dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_REPLACEMENT' "$replacement"
dotnet build Probe.csproj --nologo > logs/normal-build.log 2>&1
build=$?; record 'dotnet build Probe.csproj --nologo' "$build"
if [ "$build" -ne 0 ]; then exit "$build"; fi
dotnet run --project Probe.csproj --no-build > logs/normal-run.log 2>&1
run=$?; record 'dotnet run --project Probe.csproj --no-build' "$run"
if [ "$bad" -eq 0 ]; then echo 'BAD_BINDING unexpectedly compiled' >> logs/exit-codes.log; exit 2; fi
if [ "$replacement" -eq 0 ]; then echo 'BAD_REPLACEMENT unexpectedly compiled' >> logs/exit-codes.log; exit 2; fi
exit "$run"
