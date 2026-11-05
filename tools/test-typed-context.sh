#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
evidence=".scratch/ticket-18/compiler"
mkdir -p "$evidence"
project=tests/TypedContextCompile/TypedContextCompile.csproj
dotnet build "$project" -p:DefineConstants= > "$evidence/positive.txt" 2>&1
dotnet run --no-build --project "$project" > "$evidence/run.txt" 2>&1
for entry in BAD_BINDING:CS0266 BAD_INPUT:CS1503 BAD_INIT:CS8852 BAD_PRIVATE:CS0272 BAD_READONLY:CS0200 BAD_CONTEXT_REBIND:CS0200 BAD_BIND_ENTRY:CS1061; do
  symbol=${entry%:*}
  diagnostic=${entry#*:}
  if dotnet build "$project" --no-restore -p:DefineConstants="$symbol" > "$evidence/$symbol.txt" 2>&1; then
    echo "FAIL: $symbol unexpectedly compiled" >&2
    exit 1
  fi
  grep -q "$diagnostic" "$evidence/$symbol.txt" || { echo "FAIL: $symbol missing $diagnostic" >&2; exit 1; }
  echo "PASS: $symbol rejected with $diagnostic"
done
dotnet build "$project" --no-restore -p:DefineConstants= > "$evidence/final-positive.txt" 2>&1
echo 'PASS: positive builds and real compiler negative cases'
