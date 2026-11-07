"""Offline contract tests through the acceptance runner's public CLI."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import threading
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

RUNNER = Path(__file__).resolve().parents[1] / "acceptance.py"


class AcceptanceCliTests(unittest.TestCase):
    def observer(self, box, suite="boundary"):
        startup = {"boundary": "BoundaryHostStartup", "source": "SourceBoundaryStartup", "complex": "ComplexHostAcceptance", "legacy": "HostAcceptance"}[suite]
        jar = box / "plugins/anotherfsm-acceptance/lib/acceptance.jar"
        jar.parent.mkdir(parents=True)
        with zipfile.ZipFile(jar, "w") as archive:
            archive.writestr("META-INF/plugin.xml", '<idea-plugin><extensions defaultExtensionNs="com.intellij">'
                             '<postStartupActivity implementation="anotherfsm.acceptance.' + startup + '"/>'
                             '<postStartupActivity implementation="anotherfsm.acceptance.AcceptanceLifecycle"/>'
                             '</extensions></idea-plugin>')
            archive.writestr("anotherfsm/acceptance/" + startup + ".class", b"controlled test class")
            archive.writestr("anotherfsm/acceptance/AcceptanceLifecycle.class", b"controlled lifecycle class")
        return jar

    def cli(self, box, suite="boundary", extra=()):
        return subprocess.run([sys.executable, str(RUNNER), "check", "--suite", suite, "--sandbox", str(box), *extra],
                              text=True, capture_output=True, timeout=10)

    def prepared(self, box, suite="boundary", source=None):
        from acceptance_contract import record_preparation
        self.observer(box, suite)
        backend = box / "plugins/anotherfsm-acceptance/dotnet/observer.dll"
        backend.parent.mkdir(parents=True)
        backend.write_bytes(b"controlled observer")
        product = box / "plugins/anotherfsm/product.bin"
        product.parent.mkdir(parents=True)
        product.write_bytes(b"frozen product")
        (box / "prepared.json").write_text(json.dumps({
            "zipSha256": "a" * 64, "version": "2026.2.3.1", "build": "262.10968.170",
            "installedSha256": {"product.bin": hashlib.sha256(product.read_bytes()).hexdigest()}}), encoding="utf-8")
        record_preparation(box, suite, source_preparation=source)

    def test_observer_without_independent_lifecycle_is_rejected_before_launch(self):
        with tempfile.TemporaryDirectory() as temporary:
            box = Path(temporary)
            self.prepared(box)
            jar = box / "plugins/anotherfsm-acceptance/lib/acceptance.jar"
            with zipfile.ZipFile(jar) as archive:
                entries = {name: archive.read(name) for name in archive.namelist()
                           if name != "anotherfsm/acceptance/AcceptanceLifecycle.class"}
            with zipfile.ZipFile(jar, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
            result = self.cli(box)
            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
            self.assertEqual(json.loads(result.stdout)["code"], "observerLifecycle")
            self.assertFalse((box / "launcher.json").exists())

    def test_complete_preparation_is_ready_without_starting_a_host(self):
        with tempfile.TemporaryDirectory() as temporary:
            box = Path(temporary)
            self.prepared(box)
            result = self.cli(box)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(json.loads(result.stdout)["status"], "ready")
            self.assertFalse((box / "launcher.json").exists())

    def test_drift_since_preparation_is_rejected_before_host_start(self):
        for drift, code in [("observer", "observerChanged"), ("fixture", "fixtureIdentity"), ("product", "productPreparation")]:
            with self.subTest(drift=drift), tempfile.TemporaryDirectory() as temporary:
                box = Path(temporary)
                self.prepared(box)
                if drift == "observer":
                    (box / "plugins/anotherfsm-acceptance/dotnet/observer.dll").write_bytes(b"modified")
                elif drift == "product":
                    (box / "plugins/anotherfsm/product.bin").write_bytes(b"modified")
                else:
                    path = box / "acceptance-prepared.json"
                    manifest = json.loads(path.read_text(encoding="utf-8"))
                    key = next(iter(manifest["fixtureInputs"]))
                    manifest["fixtureInputs"][key] = "0" * 64
                    path.write_text(json.dumps(manifest), encoding="utf-8")
                result = self.cli(box)
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["code"], code)
                self.assertFalse((box / "launcher.json").exists())

    def test_old_source_service_identity_fails_before_host_start(self):
        class OldFixture(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200)
                self.send_header("X-AnotherFSM-Source-Fixture", "owned-old")
                self.end_headers()
            def log_message(self, *unused):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), OldFixture)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            with tempfile.TemporaryDirectory() as temporary:
                box = Path(temporary)
                source = box / "source-prepared.json"
                source.write_text(json.dumps({"status": "prepared", "preparationId": "owned-new", "port": server.server_port}), encoding="utf-8")
                self.prepared(box, "source", source)
                result = self.cli(box, "source", ["--source-preparation", str(source)])
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["code"], "sourceServiceIdentity")
                self.assertFalse((box / "launcher.json").exists())
        finally:
            server.shutdown()
            server.server_close()
            worker.join(timeout=2)

    def test_valid_source_service_still_requires_verified_exact_backup_bytes(self):
        class OwnedFixture(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200)
                self.send_header("X-AnotherFSM-Source-Fixture", "owned-new")
                self.end_headers()
            def log_message(self, *unused):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), OwnedFixture)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            with tempfile.TemporaryDirectory() as temporary:
                box = Path(temporary)
                source = box / "source-prepared.json"
                source.write_text(json.dumps({"status": "prepared", "preparationId": "owned-new", "port": server.server_port}), encoding="utf-8")
                self.prepared(box, "source", source)
                result = self.cli(box, "source", ["--source-preparation", str(source)])
                self.assertEqual(json.loads(result.stdout)["code"], "settingsBackup")
                identity = "00000000-0000-4000-8000-000000000001"
                data = b"exact original settings, not default settings"
                original = box / "settings" / identity / "GlobalSettingsStorage.bytes"
                original.parent.mkdir(parents=True)
                original.write_bytes(data)
                settings = box / "config/resharper-host/GlobalSettingsStorage.DotSettings"
                settings.parent.mkdir(parents=True)
                settings.write_bytes(data)
                (box / "source-boundary-settings-backup.json").write_text(json.dumps({
                    "state": "active", "confirmedClosed": True, "backupId": identity,
                    "settings": str(settings), "exists": True, "byteLength": len(data),
                    "sha256": hashlib.sha256(data).hexdigest()}), encoding="utf-8")
                result = self.cli(box, "source", ["--source-preparation", str(source)])
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["status"], "ready")
                original.write_bytes(b"changed")
                result = self.cli(box, "source", ["--source-preparation", str(source)])
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["code"], "settingsBackup")
                self.assertFalse((box / "launcher.json").exists())
        finally:
            server.shutdown()
            server.server_close()
            worker.join(timeout=2)

    def test_incomplete_or_nonobject_manifest_has_a_structured_preflight_failure(self):
        for content in ["{", "null", "[]"]:
            with self.subTest(content=content), tempfile.TemporaryDirectory() as temporary:
                box = Path(temporary)
                (box / "acceptance-prepared.json").write_text(content, encoding="utf-8")
                result = self.cli(box)
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertEqual(json.loads(result.stdout)["status"], "preflightFailed")
                self.assertFalse((box / "launcher.json").exists())

    def test_wrong_prepared_suite_is_rejected_before_host_start(self):
        with tempfile.TemporaryDirectory() as temporary:
            box = Path(temporary)
            (box / "acceptance-prepared.json").write_text(
                json.dumps({"suite": "complex"}), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(RUNNER), "check", "--suite", "boundary", "--sandbox", str(box)],
                text=True, capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 2, result.stderr)
            report = json.loads(result.stdout)
            self.assertEqual(report["status"], "preflightFailed")
            self.assertEqual(report["code"], "suiteMismatch")
            self.assertFalse((box / "launcher.json").exists())

    def test_matching_mode_without_its_registered_startup_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            box = Path(temporary)
            jar = box / "plugins/anotherfsm-acceptance/lib/acceptance.jar"
            jar.parent.mkdir(parents=True)
            with zipfile.ZipFile(jar, "w") as archive:
                archive.writestr("META-INF/plugin.xml", '<idea-plugin><extensions defaultExtensionNs="com.intellij">'
                                 '<postStartupActivity implementation="anotherfsm.acceptance.ComplexHostAcceptance"/>'
                                 '</extensions></idea-plugin>')
                archive.writestr("anotherfsm/acceptance/ComplexHostAcceptance.class", b"controlled test class")
            (box / "acceptance-prepared.json").write_text(
                json.dumps({"suite": "boundary"}), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(RUNNER), "check", "--suite", "boundary", "--sandbox", str(box)],
                text=True, capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 2, result.stderr)
            self.assertEqual(json.loads(result.stdout)["code"], "observerEntrypoint")
            self.assertFalse((box / "launcher.json").exists())

    def test_observer_without_its_bound_product_preparation_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            box = Path(temporary)
            self.observer(box)
            (box / "acceptance-prepared.json").write_text(
                json.dumps({"suite": "boundary", "productZipSha256": "a" * 64}), encoding="utf-8")
            result = self.cli(box)
            self.assertEqual(result.returncode, 2, result.stderr)
            self.assertEqual(json.loads(result.stdout)["code"], "productPreparation")
            self.assertFalse((box / "launcher.json").exists())


if __name__ == "__main__":
    unittest.main()
