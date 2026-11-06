#!/usr/bin/env python3
"""Explicit build-tool restore only; never called by the installed plugin."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=ROOT / ".scratch/issue-32/cache")
    parser.add_argument("--offline", action="store_true", help="Verify local artifacts without network")
    args = parser.parse_args()
    args.directory.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((ROOT / "rider/protocol/toolchain.json").read_text(encoding="utf-8"))
    for artifact in manifest["artifacts"]:
        url = artifact["url"]
        if not url.startswith("https://repo.maven.apache.org/maven2/org/jetbrains/"):
            raise RuntimeError("Unexpected build-tool origin")
        target = args.directory / url.rsplit("/", 1)[-1]
        if target.exists():
            data = target.read_bytes()
        else:
            if args.offline:
                raise RuntimeError(f"Missing offline build tool: {target}")
            with urllib.request.urlopen(url, timeout=60) as response:
                data = response.read(artifact["size"] + 1)
        if len(data) != artifact["size"] or hashlib.sha256(data).hexdigest() != artifact["sha256"]:
            raise RuntimeError(f"Build-tool identity mismatch: {target}")
        if not target.exists():
            target.write_bytes(data)
        print(f"VERIFIED {target.name} {artifact['sha256']}")


if __name__ == "__main__":
    main()
