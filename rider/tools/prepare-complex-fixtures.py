#!/usr/bin/env python3
"""Explicit offline preparation for issue #34. Never launch Rider or fixture business code."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import traceback
import uuid

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "rider/fixtures/Complex"
SHARED = ROOT / "rider/fixtures/SharedComplex"
ARTIFACTS = FIXTURES / "artifacts"
LOG_ROOT = ROOT / ".scratch/issue-34/fixtures"
FRAMEWORK_PACKAGES = (
    ("netstandard.library", "2.0.3"),
    ("microsoft.netcore.platforms", "1.1.0"),
)
# Actual SDK 10.0.300 requirements, also present in demos/FSMDemo.Browser's assets.
# These are copied as existing nupkgs, never fetched or reconstructed from extracted files.
WASM_PACKAGES = (
    ("microsoft.net.sdk.webassembly.pack", "10.0.9"),
    ("microsoft.netcore.app.runtime.mono.browser-wasm", "10.0.9"),
    ("microsoft.net.illink.tasks", "10.0.8"),
)
# Required by MatrixWasm/net8.0. Main separately prepared the missing official runtime nupkg;
# this script only reads existing local files and never fetches any package or workload.
WASM8_PACKAGES = (
    ("microsoft.net.illink.tasks", "8.0.27"),
    ("microsoft.netcore.app.runtime.mono.browser-wasm", "8.0.28"),
)
LOCAL_TOOLCHAIN_SHA256 = {
    "microsoft.netcore.app.runtime.mono.browser-wasm.8.0.28.nupkg":
        "98cde7182c0530e6a021a32fb0e1bb2de99c77d9f8247c0f4eff6f174d921b9a",
}
MATRIX_HOSTS = {
    "MatrixLibrary": ("netstandard2.0",),
    "MatrixPlain": ("net8.0", "net10.0"),
    "MatrixWeb": ("net8.0", "net10.0"),
    "MatrixWasm": ("net8.0", "net10.0"),
}
BINARIES = {
    "DllNodes": "Issue34.HostLoadedNodes",
    "TwinLeft": "Issue34.TwinLeft",
    "TwinRight": "Issue34.TwinRight",
}


def no_links(path):
    path = Path(path).absolute()
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
            raise ValueError(f"Refusing linked path: {part}")
    return path


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def terminate_owned_process(process, output, evidence):
    """Terminate only this Popen child/tree; preserve PID/command and read back the effect."""
    native_encoding = "mbcs" if os.name == "nt" else "utf-8"
    evidence.update({"pid": process.pid, "parent_pid": os.getpid(), "command": process.args,
                     "native_output_encoding": native_encoding, "running_before": process.poll() is None})
    if evidence["running_before"]:
        if os.name == "nt":
            command = ["taskkill", "/PID", str(process.pid), "/T", "/F"]
            evidence["kill_command"] = command
            # Native Python subprocess, not bash: /PID /T /F cannot undergo MSYS path conversion.
            killed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=15, check=False)
            evidence["kill_output"] = killed.stdout.decode(native_encoding, errors="replace")
            evidence["kill_output_hex"] = killed.stdout.hex()
            output.write(evidence["kill_output"] + "\n")
            output.flush()
            evidence["kill_exit_code"] = killed.returncode
            # Each taskkill line's FIRST PID is the terminated/reported process, not its parent PID.
            reported = [re.search(rb"\bPID\s+(\d+)", line) for line in killed.stdout.splitlines()]
            evidence["reported_pids"] = sorted({process.pid, *(int(match[1]) for match in reported if match)})
        else:
            # Main preparation starts a new session for each owned child on POSIX.
            import signal
            evidence["process_group"] = process.pid
            os.killpg(process.pid, signal.SIGKILL)
    evidence["exit_code_after_wait"] = process.wait(timeout=15)
    evidence["reaped_after"] = process.poll() is not None
    if os.name == "nt":
        evidence["pid_readback"] = []
        for pid in evidence.get("reported_pids", [process.pid]):
            query = ["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"]
            result = subprocess.run(query, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=15, check=False)
            text = result.stdout.decode(native_encoding, errors="replace")
            # Nonmatching/localized INFO text has no CSV PID column; matching processes do.
            present = any(len(row) > 1 and row[1].strip() == str(pid) for row in csv.reader(text.splitlines()))
            evidence["pid_readback"].append({"pid": pid, "command": query, "exit_code": result.returncode,
                                             "output": text, "output_hex": result.stdout.hex(), "present_after": present})
            if result.returncode != 0 or present:
                raise RuntimeError(f"Timeout cleanup readback failed for owned/reported PID {pid}")
    else:
        try:
            os.killpg(process.pid, 0)
        except ProcessLookupError:
            evidence["process_group_absent_after"] = True
        else:
            raise RuntimeError(f"Timeout cleanup left process group {process.pid} present")
    evidence["verified"] = evidence["reaped_after"]


def inventory():
    files = [Path(__file__).resolve()]
    for directory in (FIXTURES, SHARED):
        files.extend(path for path in directory.rglob("*") if path.is_file()
                     and not set(path.relative_to(directory).parts) & {"bin", "obj", "artifacts", ".idea"})
    return {path.relative_to(ROOT).as_posix(): digest(no_links(path)) for path in sorted(files)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline-framework-feed", type=Path,
                        help="Existing local flat feed or SDK NuGetFallbackFolder; copies only the two pinned framework packages")
    parser.add_argument("--offline-wasm-cache", type=Path,
                        default=Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages")),
                        help="Existing local flat feed or NuGet package cache; stages pinned WASM packages offline, with net8 inputs preferring the already prepared fixture feed")
    parser.add_argument("--probe-net8-wasm", action="store_true",
                        help="Also build the standalone real net8 WebAssembly SDK probe offline; now required to succeed when requested (the seven-context matrix always builds)")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--timeout", type=int, default=110, help="Per-command seconds (15..120)")
    args = parser.parse_args()
    if not 15 <= args.timeout <= 120:
        parser.error("--timeout must be 15..120")
    logs = no_links(LOG_ROOT / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]))
    logs.mkdir(parents=True)
    no_links(ARTIFACTS).mkdir(parents=True, exist_ok=True)
    marker = logs / "business-execution.marker"
    report = {"status": "preparing", "preparation_only": True, "commands": [],
              "marker": str(marker), "host_navigation_verified": False,
              "browser_kind": "StaticWasm net10.0 and MatrixWasm net8.0;net10.0: real Microsoft.NET.Sdk.WebAssembly/browser-wasm; build only",
              "cleanup": {}}
    summary = logs / "summary.json"
    lock = ARTIFACTS / "prepare.lock"
    locked = False

    def save():
        summary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    def check_marker():
        if marker.exists():
            raise RuntimeError(f"Business execution marker exists; retained without deletion: {marker}")

    try:
        no_links(lock)
        with lock.open("x", encoding="utf-8") as stream:
            stream.write(str(os.getpid()))
        locked = True
        dotnet = shutil.which("dotnet")
        if not dotnet:
            raise RuntimeError("A locally installed .NET 8/10 SDK is required; nothing will be installed")
        env = os.environ.copy()
        env.update({"DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                    "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true", "DOTNET_NOLOGO": "1",
                    "NUGET_PACKAGES": str(ARTIFACTS / "packages"),
                    "NUGET_HTTP_CACHE_PATH": str(ARTIFACTS / "http-cache"),
                    "NUGET_PLUGINS_CACHE_PATH": str(ARTIFACTS / "plugin-cache"),
                    "NUGET_PLUGIN_PATHS": "", "NUGET_CREDENTIALPROVIDERS_PATH": "",
                    "NUGET_CERT_REVOCATION_MODE": "offline",
                    "ANOTHERFSM_COMPLEX_EXECUTION_MARKER": str(marker)})
        report["sources_before"] = inventory()
        save()

        def run(name, arguments, expected_error=None):
            check_marker()
            command = [dotnet, *map(str, arguments)]
            log = logs / (name + ".log")
            item = {"name": name, "command": command, "log": str(log), "expected_error": expected_error}
            report["commands"].append(item)
            save()
            with log.open("w", encoding="utf-8") as output:
                output.write(json.dumps(command) + "\n")
                output.flush()
                # No shell, assembly execution, dotnet run/test/publish, plugin install or workload install.
                process = subprocess.Popen(command, cwd=FIXTURES, env=env, stdout=output, stderr=subprocess.STDOUT,
                                           start_new_session=os.name != "nt")
                item["owned_process"] = {"pid": process.pid, "parent_pid": os.getpid(), "command": command}
                save()
                try:
                    code = process.wait(timeout=args.timeout)
                except subprocess.TimeoutExpired:
                    item["timed_out"] = True
                    item["timeout_cleanup"] = {}
                    save()
                    try:
                        terminate_owned_process(process, output, item["timeout_cleanup"])
                    finally:
                        save()
                    raise RuntimeError(f"Timed out: {name}; see {log}")
            item["exit_code"] = code
            check_marker()
            text = log.read_text(encoding="utf-8", errors="replace")
            item["warning_codes"] = sorted(set(re.findall(r"\bwarning\s+([A-Z]+\d+)", text)))
            item["error_codes"] = sorted(set(re.findall(r"\berror\s+([A-Z]+\d+)", text)))
            if expected_error:
                if code == 0 or expected_error not in text:
                    raise RuntimeError(f"Expected failure {expected_error} not observed: {log}")
                item["result"] = "expected_failure"
            elif code != 0:
                raise RuntimeError(f"Preparation failed: {name}; see {log}")
            else:
                item["result"] = "passed"
            save()
            return text

        def sdk_query(name, project, *queries, target_framework=None):
            arguments = ["build", project, *flags, "--no-restore", *queries]
            if target_framework:
                arguments.append("-p:TargetFramework=" + target_framework)
            text = run(name, arguments)
            match = re.search(r"(?m)^\{", text)
            if not match:
                raise RuntimeError(f"No SDK query JSON in {name}.log")
            return json.JSONDecoder().raw_decode(text[match.start():])[0]

        run("sdk-list", ["--list-sdks"])
        feed = no_links(ARTIFACTS / "feed")
        feed.mkdir(exist_ok=True)
        fallback = Path(dotnet).resolve().parent / "sdk/NuGetFallbackFolder"
        def stage_packages(packages, directories, report_key):
            report[report_key] = []
            for package, version in packages:
                filename = f"{package}.{version}.nupkg"
                candidates = []
                for directory in directories:
                    candidates.extend((directory / filename, directory / package / version / filename))
                candidates.append(feed / filename)
                source = next((no_links(path) for path in candidates if path.is_file()), None)
                if source is None:
                    raise RuntimeError(f"Offline prerequisite missing: {filename}; searched {[str(path) for path in candidates]}. No network/SDK/workload installation is permitted")
                destination = no_links(feed / filename)
                source_hash = digest(source)
                expected_hash = LOCAL_TOOLCHAIN_SHA256.get(filename)
                if expected_hash and source_hash != expected_hash:
                    raise RuntimeError(f"Local toolchain package hash mismatch: {source}; expected {expected_hash}, got {source_hash}")
                if source != destination:
                    shutil.copyfile(source, destination)
                copied_hash = digest(destination)
                report[report_key].append({"source": str(source), "file": str(destination),
                                           "source_sha256": source_hash, "sha256": copied_hash,
                                           "expected_sha256": expected_hash,
                                           "copy_readback_matches": source_hash == copied_hash})
                save()
                if copied_hash != source_hash:
                    raise RuntimeError(f"Offline package copy readback mismatch: {destination}")

        stage_packages(FRAMEWORK_PACKAGES, [args.offline_framework_feed or fallback], "offline_framework_packages")
        stage_packages(WASM_PACKAGES, [args.offline_wasm_cache], "offline_wasm_packages")
        stage_packages(WASM8_PACKAGES, [feed, args.offline_wasm_cache], "offline_net8_wasm_packages")
        flags = ["--configuration", args.configuration, "--nologo", "-m:1", "-nr:false",
                 "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-p:RestoreIgnoreFailedSources=false"]
        binary_dir = no_links(ARTIFACTS / "dll")
        binary_dir.mkdir(exist_ok=True)
        for project, assembly in BINARIES.items():
            run("build-" + project, ["build", f"{project}/{project}.csproj", *flags])
            source = FIXTURES / project / "bin" / args.configuration / "netstandard2.0" / (assembly + ".dll")
            shutil.copyfile(no_links(source), no_links(binary_dir / source.name))
        # Remove only our controlled package cache, so editing/repacking the same pinned version is effective.
        cache = no_links(ARTIFACTS / "packages/issue34.localnodes")
        cleanup = {"path": str(cache), "existed_before": cache.exists()}
        report["cleanup"]["local_package_cache"] = cleanup
        save()
        if cleanup["existed_before"]:
            for path in cache.rglob("*"):
                no_links(path)
            shutil.rmtree(cache)
        no_links(cache)
        cleanup["absent_after"] = not os.path.lexists(cache)
        cleanup["absent_from_parent_listing"] = (not cache.parent.exists() or
            cache.name.casefold() not in {path.name.casefold() for path in cache.parent.iterdir()})
        save()
        if not cleanup["absent_after"] or not cleanup["absent_from_parent_listing"]:
            raise RuntimeError(f"Controlled package cache deletion readback failed: {cache}")
        run("build-package", ["build", "PackageNodes/PackageNodes.csproj", *flags])
        run("pack-local", ["pack", "PackageNodes/PackageNodes.csproj", *flags,
                           "--no-build", "--no-restore", "--output", feed])
        run("build-static-wasm", ["build", "StaticWasm/StaticWasm.csproj", *flags])
        context = sdk_query("wasm-sdk-context", "StaticWasm/StaticWasm.csproj",
                            "-getProperty:UsingMicrosoftNETSdkWebAssembly,TargetFramework,RuntimeIdentifier,OutputType,WasmBuildNative,TargetPath")
        report["wasm_sdk_context"] = context["Properties"]
        for key, expected in {"UsingMicrosoftNETSdkWebAssembly": "true", "TargetFramework": "net10.0",
                              "RuntimeIdentifier": "browser-wasm", "OutputType": "Exe", "WasmBuildNative": "false"}.items():
            if context["Properties"].get(key) != expected:
                raise RuntimeError(f"Unexpected actual WASM SDK property {key}: {context}")
        run("build-complex", ["build", "Complex.sln", *flags])
        report["source_matrix"] = []
        matrix_outputs = []
        shared_cs = (SHARED / "SourceMatrix.cs").resolve()
        shared_dsl = (SHARED / "source-matrix.fsm").resolve()
        for project, frameworks in MATRIX_HOSTS.items():
            for tfm in frameworks:
                evaluated = sdk_query("matrix-" + project + "-" + tfm, f"{project}/{project}.csproj",
                    "-getProperty:TargetFramework,AssemblyName,TargetPath,UsingMicrosoftNETSdkWeb,UsingMicrosoftNETSdkWebAssembly,RuntimeIdentifier,OutputType,WasmBuildNative",
                    "-getItem:ProjectReference,Reference,PackageReference,Compile,None", target_framework=tfm)
                properties, items = evaluated["Properties"], evaluated["Items"]
                project_references = {Path(item["FullPath"]).resolve() for item in items["ProjectReference"]}
                expected_references = {(FIXTURES / "StaticContract/StaticContract.csproj").resolve(),
                                       (FIXTURES / "MatrixDependency/MatrixDependency.csproj").resolve()}
                binary_references = [item for item in items["Reference"] if item["Identity"] == "Issue34.HostLoadedNodes"]
                packages = [item for item in items["PackageReference"] if item["Identity"] == "Issue34.LocalNodes"]
                compile_items = [item for item in items["Compile"] if Path(item["FullPath"]).resolve() == shared_cs]
                script_items = [item for item in items["None"] if Path(item["FullPath"]).resolve() == shared_dsl]
                if (project_references != expected_references or len(binary_references) != 1 or
                        len(packages) != 1 or packages[0].get("Version") != "[1.0.0]" or
                        len(compile_items) != 1 or len(script_items) != 1 or
                        properties["TargetFramework"] != tfm or properties["AssemblyName"] != project):
                    raise RuntimeError(f"Incomplete source matrix project/TFM: {project}/{tfm}; see matrix query log")
                hint = (FIXTURES / project / binary_references[0]["HintPath"]).resolve()
                if hint != (binary_dir / "Issue34.HostLoadedNodes.dll").resolve():
                    raise RuntimeError(f"Unexpected matrix binary origin: {hint}")
                if project == "MatrixWeb" and (properties["UsingMicrosoftNETSdkWeb"] != "true" or properties["OutputType"] != "Library"):
                    raise RuntimeError(f"MatrixWeb is not the real static Web SDK: {properties}")
                if project == "MatrixWasm" and (properties["UsingMicrosoftNETSdkWebAssembly"] != "true" or
                        properties["RuntimeIdentifier"] != "browser-wasm" or properties["OutputType"] != "Exe" or properties["WasmBuildNative"] != "false"):
                    raise RuntimeError(f"MatrixWasm is not the real controlled WebAssembly SDK: {properties}")
                assembly = Path(properties["TargetPath"])
                matrix_outputs.append(assembly)
                if project == "MatrixWasm":
                    matrix_outputs.extend((assembly.parent / "dotnet.native.wasm", assembly.parent / "MatrixWasm.runtimeconfig.json",
                        FIXTURES / "MatrixWasm/obj" / args.configuration / tfm / "webcil/MatrixWasm.wasm"))
                report["source_matrix"].append({"project": project, "properties": properties,
                    "shared_csharp": str(shared_cs), "shared_dsl": str(shared_dsl),
                    "project_references": sorted(str(path) for path in project_references),
                    "binary_hint_path": str(hint), "package": "Issue34.LocalNodes/[1.0.0]",
                    "four_origins_verified_by_build_and_evaluation": True})
                save()
        report["source_matrix_context_count"] = len(report["source_matrix"])
        report["source_matrix_origin_pair_count"] = 4 * len(report["source_matrix"])
        if args.probe_net8_wasm:
            run("probe-net8-wasm", ["build", "FailureCases/Net8WasmProbe/Net8WasmProbe.csproj", *flags])
            report["net8_wasm_probe"] = {key: report["commands"][-1][key]
                                        for key in ("command", "log", "exit_code", "result", "warning_codes", "error_codes")}
            probe = sdk_query("net8-wasm-probe-context", "FailureCases/Net8WasmProbe/Net8WasmProbe.csproj",
                              "-getProperty:UsingMicrosoftNETSdkWebAssembly,TargetFramework,RuntimeIdentifier,TargetPath")
            report["net8_wasm_probe"]["sdk_context"] = probe["Properties"]
            for key, expected in {"UsingMicrosoftNETSdkWebAssembly": "true", "TargetFramework": "net8.0",
                                  "RuntimeIdentifier": "browser-wasm"}.items():
                if probe["Properties"].get(key) != expected:
                    raise RuntimeError(f"Unexpected successful net8 probe SDK context: {probe}")
            probe_assembly = Path(probe["Properties"]["TargetPath"])
            matrix_outputs.extend((probe_assembly, probe_assembly.parent / "Net8WasmProbe.runtimeconfig.json",
                                   probe_assembly.parent / "dotnet.native.wasm"))
            save()
        report["tfm_exclusive_item"] = {}
        for tfm in ("net8.0", "net10.0"):
            items = sdk_query("items-" + tfm, "MultiTarget/MultiTarget.csproj", "-getItem:None",
                              "-getProperty:TargetFramework", target_framework=tfm)
            exclusive_items = {}
            for filename in ("only-eight.fsm", "net8-only.fsm"):
                matches = [item["FullPath"] for item in items["Items"]["None"]
                           if Path(item["FullPath"]).resolve() == (FIXTURES / "MultiTarget" / filename).resolve()]
                exclusive_items[filename] = matches
                if len(matches) != (1 if tfm == "net8.0" else 0):
                    raise RuntimeError(f"Unexpected exclusive script membership for {tfm}/{filename}: {matches}")
            report["tfm_exclusive_item"][tfm] = {"matching_none_items": exclusive_items["only-eight.fsm"],
                                                "by_script": exclusive_items,
                                                "evaluated_tfm": items["Properties"]["TargetFramework"]}
            save()
        run("negative-missing-dll", ["build", "FailureCases/MissingDll/MissingDll.csproj", *flags], "CS0246")
        run("negative-missing-package", ["build", "FailureCases/MissingPackage/MissingPackage.csproj", *flags], "NU1102")
        check_marker()
        report["sources_after"] = inventory()
        if report["sources_after"] != report["sources_before"]:
            raise RuntimeError("Fixture sources changed during preparation")
        # The real WebAssembly SDK sets AppendRuntimeIdentifierToOutputPath=false.
        # Use its queried TargetPath, not an assumed browser-wasm output directory.
        wasm_assembly = Path(context["Properties"]["TargetPath"])
        wasm_output = wasm_assembly.parent
        outputs = [*binary_dir.glob("*.dll"), *matrix_outputs,
                   FIXTURES / "MatrixDependency/bin" / args.configuration / "netstandard2.0/MatrixDependency.dll",
                   feed / "Issue34.LocalNodes.1.0.0.nupkg",
                   wasm_assembly, wasm_output / "StaticWasm.runtimeconfig.json", wasm_output / "dotnet.native.wasm",
                   FIXTURES / "StaticWasm/obj" / args.configuration / "net10.0/webcil/StaticWasm.wasm"]
        report["artifacts"] = {path.relative_to(ROOT).as_posix(): digest(no_links(path)) for path in outputs}
        report["marker_absent"] = True
        report["status"] = "prepared"
        save()
        print(f"Prepared controlled fixtures only. No plugin or business execution. Evidence: {summary}")
        return 0
    except Exception as error:
        report["status"] = "failed"
        report["error"] = str(error)
        save()
        (logs / "failure.log").write_text(traceback.format_exc(), encoding="utf-8")
        print(f"Preparation failed: {error}\nEvidence: {summary}", file=sys.stderr)
        return 1
    finally:
        if locked:
            lock.unlink()


if __name__ == "__main__":
    sys.exit(main())
