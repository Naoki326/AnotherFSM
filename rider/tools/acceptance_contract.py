"""Shared suite identity for preparation, preflight and supervised execution."""
from dataclasses import dataclass
from pathlib import Path
import hashlib
import http.client
import json
import os
import uuid
from build import BUILD, VERSION, no_links
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]


@dataclass(frozen=True)
class Suite:
    fixture: str
    startup: str


SUITES = {
    "legacy": Suite("rider/fixtures/Acceptance.sln", "HostAcceptance"),
    "complex": Suite("rider/fixtures/Complex/Complex.sln", "ComplexHostAcceptance"),
    "boundary": Suite("rider/tests/fixtures/boundary/Boundary.sln", "BoundaryHostStartup"),
    "source": Suite("rider/tests/fixtures/boundary/Boundary.sln", "SourceBoundaryStartup"),
}


class PreflightError(ValueError):
    def __init__(self, code, reason):
        super().__init__(reason)
        self.code = code


SOURCE_PREPARATION = ROOT / "rider/tests/fixtures/boundary/SourceCases/artifacts/prepared.json"


def verify_prepared_artifacts(box: Path, suite: str):
    proof = json.loads(no_links(box / "acceptance-prepared.json").read_text(encoding="utf-8"))
    if not isinstance(proof, dict):
        raise PreflightError("invalidPreparation", "Preparation manifest must be a JSON object")
    if proof.get("suite") != suite:
        raise PreflightError("suiteMismatch", "Prepared suite is " + str(proof.get("suite")) + "; requested " + suite)
    verify_entrypoint(box, suite)
    verify_product(box, proof)
    verify_identity(box, suite, proof)
    return proof


def check_preparation(box: Path, suite: str, source_preparation: Path | None = None):
    proof = verify_prepared_artifacts(box, suite)
    if suite == "source":
        verify_source_service(source_preparation or SOURCE_PREPARATION, proof)
        verify_source_backup(box, source_preparation or SOURCE_PREPARATION)
    return proof


def suite_flags(suite: str):
    return {"legacy": [], "complex": ["--complex"],
            "boundary": ["--complex", "--boundaries"],
            "source": ["--complex", "--boundaries", "--source-boundaries"]}[suite]


def verify_product(box: Path, preparation: dict):
    try:
        product = json.loads((box / "prepared.json").read_text(encoding="utf-8"))
        if product["zipSha256"] != preparation["productZipSha256"]:
            raise ValueError("Observer belongs to a different product ZIP")
        if (product.get("version"), product.get("build")) != (VERSION, BUILD):
            raise ValueError("Prepared product belongs to a different Rider version")
        installed = box / "plugins/anotherfsm"
        actual = inventory(installed)
        if not actual or actual != product["installedSha256"]:
            raise ValueError("Installed product files differ from the prepared ZIP")
    except (OSError, ValueError, KeyError) as error:
        raise PreflightError("productPreparation", str(error)) from error


def verify_entrypoint(box: Path, suite: str):
    name = "anotherfsm.acceptance." + SUITES[suite].startup
    jar = box / "plugins/anotherfsm-acceptance/lib/acceptance.jar"
    no_links(jar)
    with zipfile.ZipFile(jar) as archive:
        descriptor = ET.fromstring(archive.read("META-INF/plugin.xml"))
        registered = {item.attrib.get("implementation") for item in descriptor.findall(".//postStartupActivity")}
        if name not in registered or name.replace(".", "/") + ".class" not in archive.namelist():
            raise PreflightError("observerEntrypoint", "Prepared observer does not register and contain " + name)
        lifecycle = "anotherfsm.acceptance.AcceptanceLifecycle"
        if lifecycle not in registered or lifecycle.replace(".", "/") + ".class" not in archive.namelist():
            raise PreflightError("observerLifecycle", "Independent owned-run liveness/exit observer is missing")


def inventory(directory: Path):
    no_links(directory)
    result = {}
    for parent, directories, files in os.walk(directory, followlinks=False):
        for name in directories:
            no_links(Path(parent) / name)
        for name in files:
            path = no_links(Path(parent) / name)
            result[path.relative_to(directory).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def fixture_inputs(suite: str):
    roots = {"legacy": ("rider/fixtures", "StateMachine"),
             "complex": ("rider/fixtures/Complex", "rider/fixtures/SharedComplex"),
             "boundary": ("rider/tests/fixtures/boundary",),
             "source": ("rider/tests/fixtures/boundary",)}[suite]
    result = {}
    excluded = {"bin", "obj", "artifacts", ".packages", ".idea"}
    for relative in roots:
        root = no_links(ROOT / relative)
        for parent, directories, files in os.walk(root, followlinks=False):
            directories[:] = [name for name in directories if name not in excluded]
            for name in directories:
                no_links(Path(parent) / name)
            for name in files:
                path = no_links(Path(parent) / name)
                if path.suffix.lower() in {".sln", ".csproj", ".cs", ".fsm", ".fsmscript", ".props", ".targets", ".projitems", ".json", ".config"}:
                    result[path.relative_to(ROOT).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def fixture_assets(suite: str):
    if suite == "legacy":
        return {}
    # Assets already built/loaded by the controlled fixture, not an extra assembly loader.
    root = ROOT / "rider/fixtures/Complex/artifacts"
    result = {}
    for directory in (root / "dll", root / "feed"):
        if not directory.is_dir():
            continue
        for path in directory.iterdir():
            no_links(path)
            if path.is_file() and path.name.startswith("Issue34.") and path.suffix.lower() in {".dll", ".pdb", ".nupkg"}:
                result[path.relative_to(ROOT).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def write_json(path: Path, value):
    no_links(path)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def record_preparation(box: Path, suite: str, source_preparation: Path | None = None):
    """Record actual completed preparation; never install, launch, build or execute a fixture."""
    verify_entrypoint(box, suite)
    product = json.loads(no_links(box / "prepared.json").read_text(encoding="utf-8"))
    proof = {"schemaVersion": 1, "suite": suite, "productZipSha256": product["zipSha256"],
             "fixture": (ROOT / SUITES[suite].fixture).as_posix(), "fixtureInputs": fixture_inputs(suite),
             "fixtureAssets": fixture_assets(suite),
             "observerSha256": inventory(box / "plugins/anotherfsm-acceptance")}
    verify_product(box, proof)
    if suite == "source":
        path = no_links(source_preparation or SOURCE_PREPARATION)
        metadata = json.loads(path.read_text(encoding="utf-8"))
        if metadata.get("status") != "prepared":
            raise PreflightError("sourcePreparation", "Explicitly prepare the controlled source assets first")
        proof["sourcePreparationId"] = metadata["preparationId"]
        proof["sourcePreparationSha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        proof["sourceAssets"] = inventory(path.parent / "dll")
    write_json(box / "acceptance-prepared.json", proof)
    return proof


def verify_source_backup(box: Path, source_preparation: Path):
    try:
        backup = json.loads(no_links(box / "source-boundary-settings-backup.json").read_text(encoding="utf-8"))
        if backup.get("state") != "active" or not backup.get("confirmedClosed"):
            raise ValueError("A fresh backup while the host is closed is required")
        identity = str(uuid.UUID(backup["backupId"]))
        expected = box / "config/resharper-host/GlobalSettingsStorage.DotSettings"
        if Path(backup["settings"]).absolute() != expected.absolute():
            raise ValueError("Backup belongs to another sandbox settings layer")
        data = no_links(source_preparation.parent / "settings" / identity / "GlobalSettingsStorage.bytes").read_bytes()
        if hashlib.sha256(data).hexdigest() != backup["sha256"] or len(data) != backup["byteLength"]:
            raise ValueError("Backup bytes changed")
        if expected.exists() != backup["exists"] or (backup["exists"] and no_links(expected).read_bytes() != data):
            raise ValueError("Settings changed since the prelaunch backup")
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise PreflightError("settingsBackup", str(error)) from error


def verify_source_service(path: Path, proof: dict):
    data = no_links(path).read_bytes()
    metadata = json.loads(data)
    if hashlib.sha256(data).hexdigest() != proof.get("sourcePreparationSha256") or metadata.get("preparationId") != proof.get("sourcePreparationId"):
        raise PreflightError("sourcePreparationChanged", "Source assets were prepared again; prepare their observer again")
    if inventory(path.parent / "dll") != proof.get("sourceAssets"):
        raise PreflightError("sourcePreparationChanged", "Source DLL/PDB assets changed after preparation")
    port = metadata["port"]
    if not isinstance(port, int) or not 1024 <= port <= 65535:
        raise PreflightError("sourcePreparation", "Expected a bounded unprivileged loopback fixture port")
    # Direct loopback only: no proxy, redirects, credentials or source-download requests.
    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=2)
    try:
        connection.request("GET", "/health")
        response = connection.getresponse()
        if response.status != 200 or response.getheader("X-AnotherFSM-Source-Fixture") != metadata["preparationId"]:
            raise PreflightError("sourceServiceIdentity", "Loopback service is not the prepared owned fixture")
        response.close()
    except OSError as error:
        raise PreflightError("sourceServiceUnavailable", "Prepared loopback source service is unavailable") from error
    finally:
        connection.close()


def verify_identity(box: Path, suite: str, proof: dict):
    if proof.get("schemaVersion") != 1:
        raise PreflightError("invalidPreparation", "Prepare the observer with the current runner")
    if inventory(box / "plugins/anotherfsm-acceptance") != proof.get("observerSha256"):
        raise PreflightError("observerChanged", "Observer files changed after preparation")
    if (proof.get("fixture") != (ROOT / SUITES[suite].fixture).as_posix()
            or proof.get("fixtureInputs") != fixture_inputs(suite) or proof.get("fixtureAssets") != fixture_assets(suite)):
        raise PreflightError("fixtureIdentity", "Controlled fixture inputs changed after preparation")
