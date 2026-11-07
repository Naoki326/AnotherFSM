#!/usr/bin/env python3
"""Controlled Rider acceptance. The check command never starts or modifies a host."""
import argparse
import contextlib
import json
import math
import sys
import subprocess
import uuid
sys.dont_write_bytecode = True
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET
from acceptance_contract import ROOT, SUITES, PreflightError, check_preparation, verify_prepared_artifacts, suite_flags, write_json
from build import no_links


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "check", "run"))
    parser.add_argument("--suite", choices=SUITES, required=True)
    parser.add_argument("--sandbox", type=Path, default=Path(__file__).resolve().parents[1] / ".sandbox")
    parser.add_argument("--source-preparation", type=Path, help="Read-only source fixture manifest for check")
    parser.add_argument("--rider-home", type=Path, help="Pinned Rider installation for prepare")
    parser.add_argument("--startup-timeout", type=float, default=90, help="Seconds to observe the owned lifecycle")
    parser.add_argument("--timeout", type=float, default=900, help="Total supervised host seconds")
    parser.add_argument("--cleanup-timeout", type=float, default=45, help="Seconds to confirm cooperative frontend/backend exit")
    args = parser.parse_args()
    if args.action != "check" and (args.sandbox.absolute() != (ROOT / "rider/.sandbox").absolute() or args.source_preparation):
        parser.error("Sandbox/source overrides are read-only check inputs")
    try:
        if args.action == "prepare":
            logs = no_links(ROOT / ".scratch/rider-acceptance" / ("prepare-" + uuid.uuid4().hex))
            logs.mkdir(parents=True)
            command = [sys.executable, "-u", str(ROOT / "rider/tools/prepare-acceptance.py"), *suite_flags(args.suite)]
            if args.rider_home:
                command.extend(["--rider-home", args.rider_home.as_posix()])
            with (logs / "prepare.log").open("x", encoding="utf-8") as output:
                result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, timeout=660)
            if result.returncode:
                print(json.dumps({"status": "preparationFailed", "log": str(logs / "prepare.log")}))
                return 2
            print(json.dumps({"status": "prepared", "suite": args.suite, "log": str(logs / "prepare.log"),
                              "manifest": str(args.sandbox / "acceptance-prepared.json")}))
            return 0
        if args.action == "run":
            from acceptance_resources import RunResources
            from acceptance_session import SessionHost, run_session
            if args.rider_home:
                parser.error("Use RIDER_HOME consistently for preparation and execution")
            if any(not math.isfinite(value) or not 0 < value <= 3600 for value in
                   (args.startup_timeout, args.timeout, args.cleanup_timeout)):
                parser.error("Timeouts must be finite and within 0..3600 seconds")
            proof = verify_prepared_artifacts(args.sandbox, args.suite)
            logs = no_links(ROOT / ".scratch/rider-acceptance" / ("run-" + uuid.uuid4().hex))
            resources = RunResources(args.suite, logs / "resources")
            resources.open(proof)
            result = None
            cleanup = None
            try:
                with (logs / "supervisor.log").open("x", encoding="utf-8") as output, contextlib.redirect_stdout(output):
                    result = run_session(args.suite, logs, args.startup_timeout, args.timeout,
                                         session_host=SessionHost(cleanup_timeout=args.cleanup_timeout))
            finally:
                receipt = logs / "cleanup-receipt.json"
                stopped = False
                if receipt.is_file():
                    try:
                        stopped = json.loads(receipt.read_text(encoding="utf-8")).get("hostsStopped") is True
                    except (OSError, ValueError):
                        pass
                cleanup = resources.close(hosts_stopped=stopped)
            result["resources"] = cleanup
            write_json(logs / "result.json", result)
            print(json.dumps({"status": result["status"], "suite": args.suite, "runId": result["runId"],
                              "artifactHash": result["artifactHash"], "hostsStopped": result["hostsStopped"],
                              "resourceErrors": cleanup["errors"], "report": str(logs / "result.json")}))
            if result["cleanupBlocked"] or cleanup["errors"]:
                return 5
            return {"succeeded": 0, "assertionFailed": 1, "notStarted": 3, "verificationPending": 4,
                    "heartbeatStopped": 3, "sessionError": 2}[result["status"]]
        prepared = check_preparation(args.sandbox, args.suite, args.source_preparation)
        print(json.dumps({"status": "ready", "suite": args.suite, "preflightComplete": True,
                          "productZipSha256": prepared["productZipSha256"]}))
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, ET.ParseError, subprocess.TimeoutExpired) as error:
        print(json.dumps({"status": "preflightFailed", "code": getattr(error, "code", "invalidPreparation"), "reason": str(error)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
