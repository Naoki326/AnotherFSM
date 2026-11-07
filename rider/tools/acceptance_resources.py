"""Explicit resources owned by one controlled acceptance run; no daily Rider state."""
import contextlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import time
from types import SimpleNamespace

import sandbox
from acceptance_contract import ROOT, SOURCE_PREPARATION, PreflightError, verify_source_service, fixture_inputs, write_json
from build import no_links


def source_tool():
    spec = importlib.util.spec_from_file_location("owned_source_preparation", ROOT / "rider/tools/prepare-source-boundary.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class RunResources:
    def __init__(self, suite: str, logs: Path):
        self.suite, self.logs = suite, logs
        self.source = source_tool()
        self.backup = None
        self.proof = None
        self.server = None
        self.server_output = None
        self.server_creation = None

    def open(self, proof: dict):
        self.proof = proof
        self.logs.mkdir(parents=True, exist_ok=True)
        try:
            # A backup is taken for every suite: even ordinary Rider runs may persist preferences.
            with (self.logs / "settings-backup.log").open("x", encoding="utf-8") as output, contextlib.redirect_stdout(output):
                self.source.backup_settings(SimpleNamespace(confirm_sandbox_closed=True), self.logs)
            self.backup = json.loads(self.source.BACKUP.read_text(encoding="utf-8"))
            if self.suite == "source":
                self._source_service(proof)
        except BaseException:
            self.close(hosts_stopped=True)
            raise

    def _source_service(self, proof):
        try:
            verify_source_service(SOURCE_PREPARATION, proof)
            write_json(self.logs / "source-service.json", {"ownership": "borrowed", "identityVerified": True})
            return
        except PreflightError as error:
            # An occupied stale/unknown endpoint is never killed or silently replaced.
            if error.code != "sourceServiceUnavailable":
                raise
        metadata = json.loads(no_links(SOURCE_PREPARATION).read_text(encoding="utf-8"))
        self.server_output = (self.logs / "source-service.log").open("x", encoding="utf-8")
        self.server = subprocess.Popen(
            [sys.executable, "-u", str(ROOT / "rider/tools/prepare-source-boundary.py"), "serve", "--port", str(metadata["port"])],
            cwd=ROOT, stdin=subprocess.DEVNULL, stdout=self.server_output, stderr=subprocess.STDOUT)
        self.server_creation = sandbox.windows_process(self.server.pid)
        write_json(self.logs / "source-service.json", {"ownership": "created", "pid": self.server.pid,
                   "creationFileTime": self.server_creation, "preparationId": metadata["preparationId"]})
        deadline = time.monotonic() + 10
        while True:
            try:
                verify_source_service(SOURCE_PREPARATION, proof)
                return
            except PreflightError as error:
                if error.code != "sourceServiceUnavailable" or self.server.poll() is not None or time.monotonic() >= deadline:
                    raise
                time.sleep(0.1)

    def close(self, *, hosts_stopped: bool):
        errors = []
        if self.server is not None:
            try:
                if self.server.poll() is None:
                    if sandbox.windows_process(self.server.pid) != self.server_creation:
                        raise RuntimeError("Owned source server identity changed; no termination attempted")
                    self.server.terminate()
                self.server.wait(timeout=10)
                if self.server.poll() is None:
                    raise RuntimeError("Owned source server exit unconfirmed")
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                errors.append(str(error))
        if self.server_output is not None:
            self.server_output.close()
        if self.backup is not None:
            if not hosts_stopped:
                errors.append("Host exit not confirmed; exact settings backup retained, no restore attempted")
            else:
                try:
                    with (self.logs / "settings-restore.log").open("a", encoding="utf-8") as output, contextlib.redirect_stdout(output):
                        self.source.restore_settings(SimpleNamespace(confirm_sandbox_closed=True, backup_id=self.backup["backupId"]), self.logs)
                    self.backup = None
                except (OSError, ValueError, RuntimeError) as error:
                    errors.append(str(error))
        if hosts_stopped and self.proof is not None:
            try:
                actual = fixture_inputs(self.suite)
                if actual != self.proof["fixtureInputs"]:
                    changed = sorted(key for key in actual.keys() | self.proof["fixtureInputs"].keys()
                                     if actual.get(key) != self.proof["fixtureInputs"].get(key))
                    errors.append("Fixture bytes differ after guarded participant cleanup: " + ", ".join(changed))
            except (OSError, ValueError) as error:
                errors.append("Fixture cleanup verification unavailable: " + str(error))
        receipt = {"hostsStopped": hosts_stopped, "settingsRestored": self.backup is None,
                   "ownedServerStopped": self.server is None or self.server.poll() is not None, "errors": errors}
        write_json(self.logs / "resource-cleanup.json", receipt)
        return receipt
