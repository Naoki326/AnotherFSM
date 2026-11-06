#!/usr/bin/env python3
"""Build/package only the Rider plugin against the pinned installed host.

No SDK download, protocol regeneration, fixture build, install or launch.
Compile the checked-in RD JVM model using explicit local compiler dependencies;
--protocol-jar can instead supply a precompiled model. The backend project links
its generated C# model. No SDK/runtime bundling or stubs.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RIDER_ROOT = ROOT / "rider"
LOGS = ROOT / ".scratch/issue-32"
OUTPUT = RIDER_ROOT / "build/tools-build"
DEFAULT_ZIP = OUTPUT / "anotherfsm.zip"
DEFAULT_RIDER = Path("C:/Users/Naoki/AppData/Local/Programs/Rider")
VERSION, BUILD = "2026.2.3.1", "262.10968.170"
PLUGIN = "anotherfsm"
PLUGIN_ID = "io.github.naoki326.anotherfsm"
OWNER = "anotherfsm-issue-32-tools-v1\n"
FIXED_TIME = (1980, 1, 1, 0, 0, 0)


def no_links(path: Path) -> Path:
    """Refuse symlinks AND Windows junction/reparse points, including parents."""
    path = path.absolute()
    for part in (path, *path.parents):
        try:
            info = part.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError(f"Refusing linked/reparse path: {part}")
    return path


def managed_directory(path: Path) -> Path:
    no_links(path)
    marker = path / ".anotherfsm-tools-owner"
    if path.exists():
        no_links(marker)
        if not marker.is_file() or marker.read_text(encoding="utf-8") != OWNER:
            raise ValueError(f"Refusing to adopt unowned directory: {path}")
    else:
        path.mkdir(parents=True)
        marker.write_text(OWNER, encoding="utf-8")
    return path


def log_directory(kind: str) -> Path:
    no_links(LOGS)
    LOGS.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
    result = LOGS / f"{kind}-{stamp}-{uuid.uuid4().hex[:8]}"
    result.mkdir()
    return result


def host(home: Path) -> tuple[Path, dict, dict]:
    home = home.expanduser().resolve(strict=True)
    info = json.loads((home / "product-info.json").read_text(encoding="utf-8"))
    if (info.get("version"), info.get("buildNumber"), info.get("productCode")) != (VERSION, BUILD, "RD"):
        raise ValueError(f"Requires Rider {VERSION} / {BUILD}; got {info.get('version')} / {info.get('buildNumber')}")
    launch = next((x for x in info["launch"] if x["os"] == "Windows" and x["arch"] == "amd64"), None)
    if not launch:
        raise ValueError("Pinned host must contain a Windows amd64 launch definition")
    return home, info, launch


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def argfile(arguments: list[str]) -> str:
    # Valid for javac and java. Paths supplied by callers use forward slashes.
    return "\n".join('"' + a.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\r", "\\r") + '"'
                     for a in arguments) + "\n"


def clean_environment() -> dict[str, str]:
    env = os.environ.copy()
    for name in ("JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH",
                 "RIDER_PROPERTIES", "RIDER_VM_OPTIONS", "IDEA_PROPERTIES", "IDEA_VM_OPTIONS"):
        env.pop(name, None)
    return env


def run(name: str, command: list[str | Path], logs: Path, timeout: int) -> None:
    args = [str(a) for a in command]
    target = logs / f"{name}.log"
    with target.open("x", encoding="utf-8") as stream:
        stream.write("COMMAND " + subprocess.list2cmdline(args) + "\nCWD " + str(ROOT) + "\n")
        stream.flush()
        try:
            result = subprocess.run(args, cwd=ROOT, env=clean_environment(), stdout=stream,
                                    stderr=subprocess.STDOUT, timeout=timeout, check=False)
        except subprocess.TimeoutExpired:
            stream.write(f"\nTIMEOUT after {timeout}s\n")
            raise RuntimeError(f"{name} timed out; see {target}") from None
    if result.returncode:
        raise RuntimeError(f"{name} exited {result.returncode}; see {target}")
    print(f"OK {name}: {target}")


def zip_bytes(entries: dict[str, bytes]) -> bytes:
    """Stable order, DOS epoch, permissions and compression for ZIP and JAR."""
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            item = zipfile.ZipInfo(name, FIXED_TIME)
            item.create_system = 3
            item.external_attr = (stat.S_IFREG | 0o644) << 16
            item.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(item, data, compresslevel=9)
    return output.getvalue()


def descriptor(data: bytes) -> str:
    plugin = ET.fromstring(data)
    version = plugin.find("idea-version")
    if plugin.findtext("id") != PLUGIN_ID or version is None or version.attrib != {
            "since-build": BUILD, "until-build": BUILD}:
        raise ValueError("plugin.xml must use the AnotherFSM id and exact pinned since/until build")
    return plugin.findtext("version") or "unknown"


def protocol_jar_payload(path: Path) -> bytes:
    with zipfile.ZipFile(path) as model:
        files = [item for item in model.infolist() if not item.is_dir()]
        contents = {item.filename: model.read(item) for item in files}
        if len(contents) != len(files):
            raise ValueError("Duplicate protocol JAR entries")
        required = {"com/anotherfsm/rider/protocol/AnotherFsmModel.class",
                    "com/anotherfsm/rider/protocol/AnotherFsmModel_GeneratedKt.class"}
        if not required.issubset(contents) or any(
                not name.startswith(("com/anotherfsm/rider/protocol/", "META-INF/")) for name in contents):
            raise ValueError("Protocol JAR must contain its generated model only, not SDK/runtime")
        return zip_bytes(contents)


def input_dependency(path: Path, suffix: str, home: Path) -> Path:
    path = path.resolve(strict=True)
    if not path.is_file() or path.suffix.lower() != suffix:
        raise ValueError(f"Expected an existing {suffix} dependency: {path}")
    if path.is_relative_to(home) or path.name.lower().startswith("jetbrains."):
        raise ValueError(f"Never package installed host/SDK dependencies: {path}")
    host_dirs = [home / "lib", home / "lib/ReSharperHost"]
    if any((directory / path.name).exists() for directory in host_dirs):
        raise ValueError(f"Dependency duplicates a host assembly/JAR: {path.name}")
    return path


def compiler_path_targets(work: Path) -> Path:
    """Map our unique artifacts root BEFORE the SDK's broader repository map.

    _SetPathMapFromSourceRoots prepends its rules even to a command-line PathMap.
    csc uses the first matching rule, not the longest prefix. Run after that SDK
    target for every project, retaining its SourceLink/NuGet/source-root mappings
    and portable PDBs while removing work-<uuid> from generated source/PDB paths.
    """
    project = ET.Element("Project")
    target = ET.SubElement(project, "Target", Name="AnotherFsmMapBuildArtifacts",
                           BeforeTargets="CoreCompile", DependsOnTargets="_SetPathMapFromSourceRoots")
    properties = ET.SubElement(target, "PropertyGroup")
    ET.SubElement(properties, "PathMap").text = (
        "$([MSBuild]::ValueOrDefault('$(MSBuildThisFileDirectory)', '').Replace(',', ',,').Replace('=', '=='))"
        "=/_/build/,$(PathMap)")
    ET.SubElement(target, "Message", Importance="High",
                  Text="AnotherFSM compiler PathMap ($(MSBuildProjectName)): $(PathMap)")
    path = work / "reproducible.targets"
    path.write_bytes(ET.tostring(project, encoding="utf-8", xml_declaration=True))
    return path


def build(args: argparse.Namespace, logs: Path) -> Path:
    home, info, _ = host(args.rider_home)
    java = home / "jbr/bin/java.exe"
    javac = home / "jbr/bin/javac.exe"
    for path in (java, javac, home / "lib/ReSharperHost/JetBrains.Platform.Core.dll"):
        if not path.is_file():
            raise ValueError(f"Missing installed host build input: {path}")
    dependencies = [input_dependency(p, ".jar", home) for p in args.frontend_dependency]
    if args.protocol_jar:
        dependencies.append(input_dependency(args.protocol_jar, ".jar", home))
    backend_dependencies = [input_dependency(p, ".dll", home) for p in args.backend_dependency]
    compiler = None
    if not args.protocol_jar:
        compiler = args.kotlin_compiler_dir.expanduser().resolve(strict=True)
        if not list(compiler.glob("kotlin-compiler-embeddable-*.jar")):
            raise ValueError(f"Missing local Kotlin compiler in {compiler}; no dependencies will be downloaded")
    output = managed_directory(OUTPUT)
    # Never clean shared bin/obj/classes or reuse stale compiler output.
    work = output / ("work-" + uuid.uuid4().hex)
    work.mkdir()
    backend = work / "backend"
    classes = work / "classes"
    classes.mkdir()
    protocol_jar = dependencies[-1] if args.protocol_jar else work / "anotherfsm-protocol.jar"
    if not args.protocol_jar:
        run("protocol-jvm", [sys.executable, "-u", RIDER_ROOT / "tools/generate-protocol.py",
            "--rider-home", home, "--kotlin-compiler-dir", compiler,
            "--compile-jvm-only", "--jvm-jar", protocol_jar], logs, args.timeout)
        dependencies.append(input_dependency(protocol_jar, ".jar", home))
    (logs / "inputs.json").write_text(json.dumps({
        "riderHome": str(home), "version": info["version"], "build": info["buildNumber"],
        "jbrRelease": (home / "jbr/release").read_text(encoding="utf-8"),
        "work": str(work), "protocolJar": str(protocol_jar),
        "kotlinCompilerDir": str(compiler) if compiler else None,
        "dependencies": {str(p): digest(p.read_bytes()) for p in dependencies + backend_dependencies},
        "scope": "plugin only; no fixture build; no IDE acceptance claim"
    }, indent=2), encoding="utf-8")
    path_targets = compiler_path_targets(work)
    (logs / path_targets.name).write_bytes(path_targets.read_bytes())
    run("backend", ["dotnet", "build", RIDER_ROOT / "backend/AnotherFSM.Rider.csproj",
        "--configuration", "Release", "--nologo", "--verbosity", "normal", "--artifacts-path", work / "dotnet",
        "--output", backend, f"-p:RiderHome={home.as_posix()}",
        f"-p:AntlrJavaExec={java.as_posix()}", "-p:CopyLocalLockFileAssemblies=true",
        "-p:Deterministic=true", "-p:ContinuousIntegrationBuild=true",
        f"-p:CustomAfterMicrosoftCommonTargets={path_targets.as_posix()}"], logs, args.timeout)
    sources = sorted((RIDER_ROOT / "frontend/src").rglob("*.java"))
    if not sources:
        raise ValueError("No frontend Java sources")
    # javac does NOT expand classpath wildcards supplied inside an @argfile.
    # Enumerate the installed public JARs; the argfile avoids Windows' 32K limit.
    host_jars = sorted((home / "lib").glob("*.jar"))
    if not host_jars:
        raise ValueError("No installed host JARs")
    compiler_args = ["-encoding", "UTF-8", "-proc:none", "--release", "21", "-classpath",
        os.pathsep.join(p.as_posix() for p in [*host_jars, *dependencies]),
        "-d", classes.as_posix(), *(p.as_posix() for p in sources)]
    compiler_file = work / "javac.args"
    compiler_file.write_text(argfile(compiler_args), encoding="utf-8")
    (logs / "javac.args").write_text(compiler_file.read_text(encoding="utf-8"), encoding="utf-8")
    run("frontend", [javac, "-J-Duser.language=en", "@" + str(compiler_file)], logs, args.timeout)
    jar_entries: dict[str, bytes] = {}
    for directory in (classes, RIDER_ROOT / "frontend/resources"):
        for path in sorted(directory.rglob("*")):
            if path.is_file():
                name = path.relative_to(directory).as_posix()
                if name in jar_entries:
                    raise ValueError(f"Duplicate JAR entry: {name}")
                jar_entries[name] = path.read_bytes()
    plugin_version = descriptor(jar_entries["META-INF/plugin.xml"])
    entries = {f"{PLUGIN}/lib/anotherfsm.jar": zip_bytes(jar_entries)}

    def add(folder: str, name: str, data: bytes) -> None:
        key = f"{PLUGIN}/{folder}/{name}"
        if key.casefold() in {k.casefold() for k in entries}:
            raise ValueError(f"Duplicate package dependency: {key}")
        entries[key] = data

    # Only plugin runtime assets, not compile-time references/SDK/build tools.
    required = {"ReSharperPlugin.AnotherFSM.dll", "AnotherFSM.Semantics.dll", "Antlr4.Runtime.Standard.dll"}
    assets = set(required)
    deps = json.loads((backend / "ReSharperPlugin.AnotherFSM.deps.json").read_text(encoding="utf-8"))
    for target in deps["targets"].values():
        for library in target.values():
            for asset in library.get("runtime", {}):
                name = Path(asset).name
                if name.lower().endswith(".dll") and (backend / name).is_file():
                    # Private=false host references are never shipped, even if copied accidentally.
                    input_dependency(backend / name, ".dll", home)
                    assets.add(name)
    for name in sorted(assets):
        path = input_dependency(backend / name, ".dll", home)
        add("dotnet", name, path.read_bytes())
    for path in backend_dependencies:
        add("dotnet", path.name, path.read_bytes())
    for path in dependencies:
        # Normalize the model JAR's ZIP metadata as well; do not modify input JARs.
        if path == protocol_jar:
            add("lib", path.name, protocol_jar_payload(path))
        else:
            add("lib", path.name, path.read_bytes())
    manifest = {"pluginId": PLUGIN_ID, "pluginVersion": plugin_version,
                "riderVersion": VERSION, "riderBuild": BUILD,
                "rdProtocolIncluded": True,
                "sha256": {k: digest(v) for k, v in sorted(entries.items())}}
    entries[f"{PLUGIN}/build-manifest.json"] = (json.dumps(manifest, sort_keys=True, indent=2) + "\n").encode()
    payload = zip_bytes(entries)
    no_links(DEFAULT_ZIP)
    temporary = work / "anotherfsm.zip"
    temporary.write_bytes(payload)
    os.replace(temporary, DEFAULT_ZIP)
    (logs / "package.json").write_text(json.dumps({"zip": str(DEFAULT_ZIP), "sha256": digest(payload),
        "entries": sorted(entries), "manifest": manifest}, indent=2), encoding="utf-8")
    print(f"PACKAGED {DEFAULT_ZIP}\nSHA256 {digest(payload)}")
    print("Not installed/launched. RD, navigation and old-list safety are NOT IDE-validated.")
    return DEFAULT_ZIP


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rider-home", type=Path, default=Path(os.environ.get("RIDER_HOME", str(DEFAULT_RIDER))))
    parser.add_argument("--kotlin-compiler-dir", type=Path, default=LOGS / "cache",
                        help="Local Kotlin compiler dependency directory; default .scratch/issue-32/cache; no downloads")
    parser.add_argument("--protocol-jar", type=Path,
                        help="Use a precompiled RD model instead of compiling the checked-in Kotlin model")
    parser.add_argument("--frontend-dependency", type=Path, action="append", default=[],
                        help="Additional own JAR: javac classpath + ZIP lib (repeatable; no host JARs)")
    parser.add_argument("--backend-dependency", type=Path, action="append", default=[],
                        help="Additional own runtime DLL to package; backend csproj must already reference it (repeatable)")
    parser.add_argument("--timeout", type=int, default=180, help="Timeout per compiler invocation, seconds")
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    logs = log_directory("build")
    try:
        build(args, logs)
    except (OSError, ValueError, RuntimeError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        (logs / "error.log").write_text(str(error) + "\n", encoding="utf-8")
        print(f"ERROR: {error}\nLogs: {logs}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
