#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .scratch/ticket-17/compile
project=tests/RemovedCompositionCompile/RemovedCompositionCompile.csproj
dotnet run --project "$project" > .scratch/ticket-17/compile/positive.log 2>&1
for case in GROUP GROUP_ONE GROUP_TWO PARALLEL PARALLEL_ONE PARALLEL_TWO HOOK DESCRIPTOR BASE_GROUP; do
  log=".scratch/ticket-17/compile/$case.log"
  if dotnet build "$project" --nologo -p:CompositionCase="$case" > "$log" 2>&1; then
    echo "FAIL: removed API $case still compiles"; exit 1
  fi
  grep -Eq 'error CS(0246|1061)' "$log" || { echo "Unexpected compilation failure: $log"; exit 1; }
  echo "PASS: $case rejected by C# compiler"
done
