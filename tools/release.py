"""Offline validation for the three-package NuGet release (no publishing)."""

import argparse
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from zipfile import ZipFile


PACKAGE_CONTENTS = {
    "Naoki.AnotherFSM": "lib/netstandard2.0/AnotherFSM.dll",
    "Naoki.AnotherFSM.ScriptAnalyzer": "analyzers/dotnet/cs/AnotherFSM.ScriptAnalyzer.dll",
    "Naoki.AnotherFSM.NodeAnalyzer": "analyzers/dotnet/cs/AnotherFSM.NodeAnalyzer.dll",
}


def parse_tag(tag):
    match = re.fullmatch(
        r"v((?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
        r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?)",
        tag,
    )
    if not match:
        raise ValueError("Use vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease (no build metadata)")
    version, prerelease = match.groups()
    if prerelease and any(
        part.isdigit() and len(part) > 1 and part.startswith("0")
        for part in prerelease.split(".")
    ):
        raise ValueError("Numeric prerelease identifiers must not have leading zeroes")
    return version, prerelease is not None


def verify_packages(tag, directory):
    version, _ = parse_tag(tag)
    directory = Path(directory)
    expected = {f"{name}.{version}.nupkg" for name in PACKAGE_CONTENTS}
    actual = {path.name for path in directory.glob("*.nupkg")}
    if actual != expected:
        raise ValueError(f"Package set mismatch: missing={sorted(expected - actual)}, extra={sorted(actual - expected)}")
    for name, assembly in PACKAGE_CONTENTS.items():
        with ZipFile(directory / f"{name}.{version}.nupkg") as archive:
            # NuGet selects different nuspec schema versions for libraries and analyzers.
            metadata = ET.fromstring(archive.read(f"{name}.nuspec")).find("{*}metadata")
            if metadata is None or metadata.findtext("{*}id") != name:
                raise ValueError(f"Invalid package ID: {name}")
            if metadata.findtext("{*}version") != version:
                raise ValueError(f"Package version does not match tag: {name}")
            if assembly not in archive.namelist() or archive.getinfo(assembly).file_size == 0:
                raise ValueError(f"Missing or empty assembly: {name}: {assembly}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    version_command = commands.add_parser("version", help="Emit GitHub Actions outputs")
    version_command.add_argument("tag")
    verify_command = commands.add_parser("verify", help="Check package IDs, versions and DLL paths")
    verify_command.add_argument("tag")
    verify_command.add_argument("directory", type=Path)
    args = parser.parse_args()
    if args.command == "version":
        version, prerelease = parse_tag(args.tag)
        print(f"version={version}\nprerelease={str(prerelease).lower()}")
    else:
        verify_packages(args.tag, args.directory)
        print("Verified all three NuGet packages")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, ET.ParseError) as error:
        print(f"Release validation failed: {error}", file=sys.stderr)
        sys.exit(1)
