#!/usr/bin/env python3
"""Explicit prepare/run for an isolated Rider sandbox; prepare NEVER launches.

Installs the built ZIP, not a staging directory. Opens the ORIGINAL repository's
Acceptance.sln, preserving its relative ProjectReferences and all fixture edits.
No daily IDE paths, settings import, credentials, sync, process termination or SDK.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
import io
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile

# Importing the sibling tool must not create a cache outside issue-32 logs.
sys.dont_write_bytecode = True
from build import (BUILD, VERSION, DEFAULT_RIDER, DEFAULT_ZIP, LOGS, PLUGIN, ROOT,
                   argfile, clean_environment, descriptor, digest, host,
                   log_directory, managed_directory, no_links)

BOX = ROOT / "rider/.sandbox"
FIXTURE = ROOT / "rider/fixtures/Acceptance.sln"
IDE_LOG = LOGS / "sandbox-ide"
SYNC_ID = "com.intellij.settingsSync"


def windows_process(pid: int) -> int | None:
    """Return live process creation FILETIME; never terminate any process."""
    if os.name != "nt":
        raise ValueError("PID checks and launching require native Windows Python")
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION only
    if not handle:
        error = ctypes.get_last_error()
        if error in (87, 1168):  # PID no longer exists
            return None
        raise OSError(error, f"Cannot inspect recorded sandbox PID {pid}; refusing unsafe operation")
    try:
        code = wintypes.DWORD()
        if not kernel.GetExitCodeProcess(handle, ctypes.byref(code)):
            raise ctypes.WinError(ctypes.get_last_error())
        if code.value != 259:  # STILL_ACTIVE
            return None
        created, exited, system, user = (wintypes.FILETIME() for _ in range(4))
        if not kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited),
                                      ctypes.byref(system), ctypes.byref(user)):
            raise ctypes.WinError(ctypes.get_last_error())
        return (created.dwHighDateTime << 32) | created.dwLowDateTime
    finally:
        kernel.CloseHandle(handle)


def assert_stopped() -> None:
    record = no_links(BOX / "launcher.json")
    if record.exists():
        previous = json.loads(record.read_text(encoding="utf-8"))
        created = windows_process(int(previous["windowsPid"]))
        if created is not None and (previous.get("creationFileTime") is None or
                                    created == previous["creationFileTime"]):
            raise ValueError(f"Sandbox PID {previous['windowsPid']} is still running; close ONLY its own window first")


@contextmanager
def operation():
    managed_directory(BOX)
    lock = no_links(BOX / ".operation-lock")
    try:
        stream = lock.open("x", encoding="ascii")
    except FileExistsError:
        raise ValueError(f"Another sandbox operation (or interrupted operation) owns {lock}; inspect before removing") from None
    try:
        with stream:
            stream.write(str(os.getpid()) + "\n")
        assert_stopped()
        yield
    finally:
        lock.unlink()


def package_entries(source: Path) -> tuple[dict[str, bytes], str]:
    payload = source.read_bytes()
    entries: dict[str, bytes] = {}
    seen: set[str] = set()
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        if sum(item.file_size for item in archive.infolist()) > 128 * 1024 * 1024:
            raise ValueError("Plugin ZIP exceeds 128 MiB uncompressed limit")
        for item in archive.infolist():
            path = PurePosixPath(item.filename)
            if ("\\" in item.filename or ":" in item.filename or path.is_absolute() or
                    any(p in (".", "..") or p.rstrip(" .") != p for p in item.filename.split("/")) or
                    len(path.parts) < 2 or path.parts[0] != PLUGIN or item.is_dir() or
                    stat.S_ISLNK(item.external_attr >> 16)):
                raise ValueError(f"Unsafe/unexpected ZIP entry: {item.filename}")
            name = PurePosixPath(*path.parts[1:]).as_posix()
            if name.casefold() in seen:
                raise ValueError(f"Duplicate ZIP entry: {item.filename}")
            seen.add(name.casefold())
            entries[name] = archive.read(item)
    manifest = json.loads(entries["build-manifest.json"])
    if (manifest.get("riderVersion"), manifest.get("riderBuild")) != (VERSION, BUILD):
        raise ValueError("ZIP manifest does not match the pinned Rider")
    inventory = {f"{PLUGIN}/{k}": digest(v) for k, v in entries.items() if k != "build-manifest.json"}
    if manifest.get("sha256") != inventory:
        raise ValueError("ZIP file inventory/hash mismatch")
    for required in ("lib/anotherfsm.jar", "dotnet/ReSharperPlugin.AnotherFSM.dll",
                     "dotnet/AnotherFSM.Semantics.dll", "dotnet/Antlr4.Runtime.Standard.dll"):
        if required not in entries:
            raise ValueError(f"Incomplete plugin ZIP: {required}")
    with zipfile.ZipFile(io.BytesIO(entries["lib/anotherfsm.jar"])) as jar:
        descriptor(jar.read("META-INF/plugin.xml"))
    return entries, digest(payload)


def installed_hashes() -> dict[str, str]:
    target = no_links(BOX / "plugins" / PLUGIN)
    result = {}
    for path in target.rglob("*"):
        no_links(path)
        if path.is_file():
            result[path.relative_to(target).as_posix()] = digest(path.read_bytes())
    return result


def launch_arguments(home: Path, launch: dict, fixture: Path = FIXTURE) -> list[str]:
    if not fixture.is_file():
        raise ValueError(f"Original repository fixture is missing: {fixture}")
    for path in (BOX / "config", BOX / "system", BOX / "plugins", IDE_LOG):
        no_links(path)
        path.mkdir(parents=True, exist_ok=True)
    vm = [line.strip() for line in (home / launch["vmOptionsFilePath"]).read_text(encoding="utf-8").splitlines()
          if line.strip() and not line.lstrip().startswith("#")]
    vm += [s.replace("%IDE_HOME%", home.as_posix()).replace("\\", "/")
           for s in launch["additionalJvmArguments"]]
    properties = {
        "idea.paths.selector": "AnotherFSM-Issue32-262-Isolated",
        "idea.home.path": home.as_posix(),
        "idea.config.path": (BOX / "config").as_posix(),
        "idea.system.path": (BOX / "system").as_posix(),
        "idea.plugins.path": (BOX / "plugins").as_posix(),
        "idea.log.path": IDE_LOG.as_posix(),
        "idea.initially.ask.config": "false",
        "idea.config.import.enabled": "false",
        "idea.show.customize.ide.wizard": "false",
        "idea.settings.sync.enabled": "false",
    }
    remove = tuple(f"-D{k}=" for k in (*properties, "idea.properties.file", "idea.paths.customizer"))
    vm = [s for s in vm if not s.startswith((*remove, "-XX:HeapDumpPath=", "-XX:ErrorFile="))]
    vm += [f"-D{k}={v}" for k, v in properties.items()]
    vm += [f"-XX:HeapDumpPath={IDE_LOG.as_posix()}", f"-XX:ErrorFile={IDE_LOG.as_posix()}/hs_err_pid%p.log"]
    # product-info's vmoptions use shell-style quoted empty values; argfile() quotes
    # each complete token, so remove these inner quotes rather than passing them literally.
    vm = [s.replace('=""', '=') for s in vm]
    boot = [home / "lib" / name for name in launch["bootClassPathJarNames"]]
    for path in (home / launch["javaExecutablePath"], *boot):
        if not path.is_file():
            raise ValueError(f"Missing installed host launch input: {path}")
    return [*vm, "-cp", ";".join(p.as_posix() for p in boot), launch["mainClass"], fixture.as_posix()]


def disable_sync() -> None:
    target = no_links(BOX / "config/disabled_plugins.txt")
    if not target.exists():
        target.write_text(SYNC_ID + "\n", encoding="utf-8")
    elif SYNC_ID not in target.read_text(encoding="utf-8").splitlines():
        # Do not overwrite user-edited sandbox configuration.
        raise ValueError(f"Sync must remain disabled; add {SYNC_ID} to {target} (file was not overwritten)")


def prepare(source: Path, home: Path, launch: dict, logs: Path) -> None:
    entries, zip_hash = package_entries(source)
    with operation():
        arguments = launch_arguments(home, launch)
        disable_sync()
        target = no_links(BOX / "plugins" / PLUGIN)
        state_file = no_links(BOX / "prepared.json")
        if target.exists():
            if not state_file.is_file():
                raise ValueError(f"Refusing to replace unowned sandbox plugin: {target}")
            previous = json.loads(state_file.read_text(encoding="utf-8"))
            if installed_hashes() != previous.get("installedSha256"):
                raise ValueError("Sandbox plugin has user modifications/extra files; refusing to overwrite")
        stage = BOX / ("install-" + uuid.uuid4().hex)
        stage.mkdir()
        try:
            # Manual extraction after validation, never ZipFile.extractall().
            for name, data in entries.items():
                path = stage / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            if target.exists():
                shutil.rmtree(target)  # Only the owned, byte-for-byte verified previous build.
            stage.rename(target)
        finally:
            if stage.exists():
                shutil.rmtree(stage)  # Unique directory created by this invocation only.
        state = {"riderHome": str(home), "version": VERSION, "build": BUILD,
                 "zip": str(source.resolve()), "zipSha256": zip_hash, "fixture": str(FIXTURE),
                 "installedSha256": {k: digest(v) for k, v in entries.items()}}
        state_file.write_text(json.dumps(state, indent=2), encoding="utf-8")
        (logs / "prepared.json").write_text(json.dumps(state, indent=2), encoding="utf-8")
        # Preview only. run regenerates arguments from the pinned host, not editable metadata.
        (logs / "launch-preview.args").write_text(argfile(arguments), encoding="utf-8")
    print(f"Prepared ONLY: {target}\nOriginal fixture: {FIXTURE}\nLogs: {logs}")
    print("Not started. Explicit launch: python rider/tools/sandbox.py run")
    print("Daily Rider untouched. Settings Sync disabled; do not import settings or sign in for sync.")


def launch_sandbox(home: Path, launch: dict, logs: Path, acceptance: bool = False, complex_contexts: bool = False,
                   boundary_contexts: bool = False, source_boundaries: bool = False, trust_probe: bool = False,
                   run_id: str | None = None, acceptance_case: str = 'all') -> dict:
    if os.name != "nt":
        raise ValueError("Run with native Windows Python to record the actual Windows Java PID")
    if run_id is not None:
        if not acceptance or str(uuid.UUID(run_id)) != run_id:
            raise ValueError('A canonical acceptance runId requires explicit acceptance mode')
    with operation():
        state_file = no_links(BOX / "prepared.json")
        if not state_file.is_file():
            raise ValueError("Run prepare first")
        state = json.loads(state_file.read_text(encoding="utf-8"))
        if (state.get("riderHome"), state.get("version"), state.get("build")) != (str(home), VERSION, BUILD):
            raise ValueError("Prepared host differs; prepare again with this pinned Rider")
        if not (BOX / "plugins" / PLUGIN).is_dir() or installed_hashes() != state.get("installedSha256"):
            raise ValueError("Prepared plugin was removed/changed; refusing launch")
        disable_sync()
        fixture = ROOT / 'rider/tests/fixtures/trust-declined/Declined.sln' if trust_probe else (
            ROOT / 'rider/tests/fixtures/boundary/Boundary.sln' if boundary_contexts else (
                ROOT / "rider/fixtures/Complex/Complex.sln" if complex_contexts else FIXTURE))
        if trust_probe and (acceptance or complex_contexts or boundary_contexts or source_boundaries):
            raise ValueError('Trust probe is a separate, decline-only native host journey')
        if (complex_contexts or boundary_contexts) and not acceptance:
            raise ValueError("Complex/boundary contexts require the explicit controlled acceptance mode")
        if source_boundaries and not boundary_contexts:
            raise ValueError('Source boundary setup requires the independent Boundary.sln')
        if not acceptance and acceptance_case != 'all':
            raise ValueError('An independent case requires explicit controlled acceptance mode')
        if acceptance:
            from acceptance_contract import check_preparation
            suite = 'source' if source_boundaries else 'boundary' if boundary_contexts else 'complex' if complex_contexts else 'legacy'
            check_preparation(BOX, suite, case=acceptance_case)
        arguments = launch_arguments(home, launch, fixture)
        environment = clean_environment()
        environment.pop("ANOTHERFSM_ACCEPTANCE_DIR", None)
        environment.pop("ANOTHERFSM_COMPLEX_EXECUTION_MARKER", None)
        environment.pop("ANOTHERFSM_ACCEPTANCE_RUN_ID", None)
        environment.pop("ANOTHERFSM_ACCEPTANCE_CASE", None)
        if acceptance:
            observer = BOX / "plugins/anotherfsm-acceptance"
            if not (observer / "lib/acceptance.jar").is_file():
                raise ValueError("Build/install the separate acceptance observer first")
            evidence = logs / "acceptance"
            evidence.mkdir()
            properties = ["-Danotherfsm.acceptance.case=" + acceptance_case, "-Danotherfsm.acceptance=true", "-Danotherfsm.transportReconnectAcceptance=true", "-Danotherfsm.acceptance.dir=" + evidence.as_posix(),
                          "-Danotherfsm.fixture.root=" + fixture.parent.as_posix()]
            if run_id is not None:
                properties += ['-Danotherfsm.acceptance.runId=' + run_id,
                               '-Danotherfsm.acceptance.suite=' + suite,
                               '-Danotherfsm.acceptance.fixture=' + fixture.as_posix()]
                environment['ANOTHERFSM_ACCEPTANCE_RUN_ID'] = run_id
            if boundary_contexts:
                properties.append("-Danotherfsm.boundaryAcceptance=true")
                if source_boundaries:
                    properties.append('-Danotherfsm.sourceBoundaryAcceptance=true')
            elif complex_contexts:
                properties.append("-Danotherfsm.complexAcceptance=true")
            arguments = properties + arguments
            environment["ANOTHERFSM_ACCEPTANCE_DIR"] = str(evidence)
            environment["ANOTHERFSM_ACCEPTANCE_CASE"] = acceptance_case
            if complex_contexts or boundary_contexts:
                environment["ANOTHERFSM_COMPLEX_EXECUTION_MARKER"] = str(evidence / 'complex-business-execution.marker')
        args_file = logs / "launch.args"
        args_file.write_text(argfile(arguments), encoding="utf-8")
        command = [str(home / launch["javaExecutablePath"]), "@" + str(args_file)]
        record = {"riderHome": str(home), "version": VERSION, "build": BUILD,
                  "productInfoSha256": digest((home / "product-info.json").read_bytes()),
                  "jbrRelease": (home / "jbr/release").read_text(encoding="utf-8"),
                  "command": command, "windowsCommandLine": subprocess.list2cmdline(command),
                  "expandedArguments": arguments, "argfileSha256": digest(args_file.read_bytes()),
                  "cwd": str(BOX), "fixture": fixture.as_posix(), "zipSha256": state["zipSha256"], "runId": run_id, "case": acceptance_case,
                  "stdout": str(logs / "stdout.log"), "stderr": str(logs / "stderr.log"),
                  "ideLog": str(IDE_LOG), "complexExecutionMarker": environment.get("ANOTHERFSM_COMPLEX_EXECUTION_MARKER")}
        (logs / "command.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
        # Inherit no JVM/property injection. Never use rider64.exe (daily instance routing).
        with (logs / "stdout.log").open("xb") as stdout, (logs / "stderr.log").open("xb") as stderr:
            process = subprocess.Popen(command, cwd=BOX, env=environment, stdout=stdout,
                                       stderr=stderr, stdin=subprocess.DEVNULL, close_fds=True)
        record["windowsPid"] = process.pid
        # Persist the PID BEFORE querying it, even if Windows process inspection fails.
        latest = no_links(BOX / "launcher.json")
        latest.write_text(json.dumps(record, indent=2), encoding="utf-8")
        (logs / "process.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
        record["creationFileTime"] = windows_process(process.pid)
        latest.write_text(json.dumps(record, indent=2), encoding="utf-8")
        (logs / "process.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
        (logs / "windows.pid").write_text(str(process.pid) + "\n", encoding="ascii")
    print(f"Launched sandbox Java Windows PID={process.pid}\nPermanent startup logs: {logs}\nIDE logs: {IDE_LOG}")
    print("Close only the sandbox's own window to exit. Startup/navigation/RD are not thereby validated.")
    return record


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "run"))
    parser.add_argument("--rider-home", type=Path, default=Path(os.environ.get("RIDER_HOME", str(DEFAULT_RIDER))))
    parser.add_argument("--zip", type=Path, default=DEFAULT_ZIP, help="prepare input ZIP (run uses the prepared installation)")
    parser.add_argument("--acceptance", action="store_true", help="run only: enable the separate controlled-fixture test plugin")
    parser.add_argument('--complex', action='store_true', help='run --acceptance only: controlled complex-context solution')
    parser.add_argument('--boundary', action='store_true', help='run --acceptance only: independent reload/policy boundary solution')
    parser.add_argument('--source-boundaries', action='store_true', help='run --acceptance --boundary only: explicit external-source policy suite')
    parser.add_argument('--trust-probe', action='store_true', help='run only: decline native trust for the known never-executed fixture')
    args = parser.parse_args()
    if args.trust_probe and (args.action != 'run' or args.acceptance or args.complex or args.boundary or args.source_boundaries):
        parser.error('--trust-probe is a separate run without --acceptance/--complex/--boundary')
    if args.source_boundaries and not args.boundary:
        parser.error('--source-boundaries requires --boundary')
    if args.complex and args.boundary:
        parser.error('--complex and --boundary are separate journeys')
    if (args.complex or args.boundary) and (args.action != "run" or not args.acceptance):
        parser.error('--complex/--boundary requires run --acceptance')
    if args.action == "run" and args.zip != DEFAULT_ZIP:
        parser.error("--zip applies only to prepare")
    if args.action == "prepare" and args.acceptance:
        parser.error("--acceptance applies only to run")
    logs = log_directory("sandbox-" + args.action)
    try:
        home, _, launch = host(args.rider_home)
        if args.action == "prepare":
            prepare(args.zip, home, launch, logs)
        else:
            launch_sandbox(home, launch, logs, args.acceptance, args.complex, args.boundary, args.source_boundaries, args.trust_probe)
    except (OSError, ValueError, RuntimeError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        (logs / "error.log").write_text(str(error) + "\n", encoding="utf-8")
        print(f"ERROR: {error}\nLogs: {logs}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
