#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
evidence="${EVIDENCE_DIR:-.scratch/ticket-22/compiler}"
mkdir -p "$evidence"
project=tests/TypedContextCompile/TypedContextCompile.csproj
dotnet build "$project" --nologo -p:DefineConstants= > "$evidence/positive.log" 2>&1
for entry in OLD_CTOR:CS1729 OLD_CTOR_ASYNC:CS1729 OLD_CTOR_ENDS:CS1729 OLD_CTOR_ENDS_ASYNC:CS1729 OLD_RESTART:CS1061 OLD_RESTART_DATA:CS1061 OLD_RESTART_NODE:CS1061 OLD_RESTART_NODE_DATA:CS1061 OLD_CONTEXT:CS1061 OLD_WRAPPER:CS0246 OLD_GENERIC_WRAPPER:CS0246 OLD_HANDLE:CS1061; do
  symbol=${entry%:*}; diagnostic=${entry#*:}
  if dotnet build "$project" --no-restore --nologo -p:DefineConstants="$symbol" > "$evidence/$symbol.log" 2>&1; then
    echo "FAIL: $symbol unexpectedly compiled"; exit 1
  fi
  grep -q "error $diagnostic" "$evidence/$symbol.log" || { echo "FAIL: $symbol missing $diagnostic"; exit 1; }
  echo "PASS: $symbol rejected with $diagnostic"
done
dotnet build "$project" --no-restore --nologo -p:DefineConstants= > "$evidence/final-positive.log" 2>&1
echo 'PASS: final API positive and 12 removed-entry compiler rejections'
