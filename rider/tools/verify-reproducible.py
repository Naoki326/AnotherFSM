#!/usr/bin/env python3
"""Compare two build evidence manifests; no file writes or IDE interaction."""
import argparse
import json

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("first")
parser.add_argument("second")
args = parser.parse_args()
with open(args.first, encoding="utf-8") as stream:
    first = json.load(stream)
with open(args.second, encoding="utf-8") as stream:
    second = json.load(stream)
a, b = first["manifest"]["sha256"], second["manifest"]["sha256"]
different = [name for name in a.keys() | b.keys() if a.get(name) != b.get(name)]
if different:
    raise SystemExit("FAIL reproducible package: " + ", ".join(sorted(different)))
if first["sha256"] != second["sha256"]:
    raise SystemExit("FAIL package metadata differs despite identical assets")
print("PASS identical plugin ZIP and all runtime assets: " + first["sha256"])
