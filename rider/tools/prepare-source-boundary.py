#!/usr/bin/env python3
"""Explicit, local-only #34 source fixture preparation. No Rider launch or business execution.

prepare: build owned DLLs and a PE/PDB-only diagnostic, stage/inspect invalid IL and provenance.
serve: main explicitly starts this loopback-only 401 service (never sends source code).
backup-settings / restore-settings: only the known sandbox GlobalSettingsStorage.DotSettings,
while main has closed the sandbox. Exact bytes/absence are restored, never default-value writes.
"""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'rider/tests/fixtures/boundary/SourceCases'
BOX = ROOT / 'rider/.sandbox'
ART = ASSETS / 'artifacts'
LOGS = ROOT / '.scratch/issue-34/source-boundary'
SETTINGS = BOX / 'config/resharper-host/GlobalSettingsStorage.DotSettings'
BACKUP = BOX / 'source-boundary-settings-backup.json'
CASES = ('offline', 'unauthorized', 'mismatch', 'nosymbols', 'broken')
sys.dont_write_bytecode = True
from build import no_links


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    no_links(path)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def closed(confirm):
    if not confirm:
        raise ValueError('Main must close frontend AND backend, then explicitly --confirm-sandbox-closed')
    latest = BOX / 'launcher.json'
    launcher = json.loads(latest.read_text(encoding='utf-8')) if latest.exists() else {}
    pid = launcher.get('windowsPid')
    known = {pid} if pid else set()
    for argument in launcher.get('expandedArguments', []):
        if not argument.startswith('-Danotherfsm.acceptance.dir='):
            continue
        output = no_links(Path(argument.split('=', 1)[1]))
        if not output.is_relative_to(ROOT / '.scratch'):
            raise ValueError('Unexpected recorded acceptance output; no settings files touched')
        for filename in ('native-observation.json', 'source-policy-observation.json', 'complex-boundary-observation.json'):
            sample = output / filename
            if sample.exists():
                backend = json.loads(sample.read_text(encoding='utf-8')).get('backendPid')
                if isinstance(backend, int) and backend > 0:
                    known.add(backend)
    if known and os.name != 'nt':
        raise ValueError('Settings backup/restore requires native Windows process verification')
    for process_id in known:
        query = subprocess.run(['tasklist', '/FI', f'PID eq {process_id}', '/FO', 'CSV', '/NH'], stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, timeout=15, check=True)
        rows = csv.reader(query.stdout.decode('mbcs', errors='replace').splitlines())
        if any(len(row) > 1 and row[1].strip() == str(process_id) for row in rows):
            raise ValueError(f'Recorded sandbox frontend/backend PID {process_id} is still live; no settings files touched')
    return pid


def backup_settings(args, logs):
    pid = closed(args.confirm_sandbox_closed)
    no_links(SETTINGS)
    if BACKUP.exists() and json.loads(BACKUP.read_text(encoding='utf-8')).get('state') == 'active':
        raise ValueError('An active exact settings backup already exists; restore it first')
    identity = str(uuid.uuid4())
    directory = no_links(ART / 'settings' / identity)
    directory.mkdir(parents=True)
    present = SETTINGS.exists()
    data = SETTINGS.read_bytes() if present else b''
    (directory / 'GlobalSettingsStorage.bytes').write_bytes(data)
    record = {'backupId': identity, 'state': 'active', 'capturedUtc': datetime.now(timezone.utc).isoformat(),
              'confirmedClosed': True, 'launcherPidAtCapture': pid, 'exists': present,
              'sha256': sha(data), 'byteLength': len(data), 'settings': SETTINGS.as_posix(),
              'scope': 'exact known sandbox global settings layer only; main must keep backend closed during restore'}
    save(BACKUP, record)
    save(logs / 'settings-backup.json', record)
    print('Settings bytes/absence backed up; backupId=' + identity)


def restore_settings(args, logs):
    closed(args.confirm_sandbox_closed)
    no_links(SETTINGS)
    record = json.loads(no_links(BACKUP).read_text(encoding='utf-8'))
    if record['state'] != 'active' or record['backupId'] != args.backup_id:
        raise ValueError('Active backupId mismatch')
    identity = str(uuid.UUID(record['backupId']))
    data = no_links(ART / 'settings' / identity / 'GlobalSettingsStorage.bytes').read_bytes()
    if sha(data) != record['sha256'] or len(data) != record['byteLength']:
        raise ValueError('Backup bytes changed')
    if record['exists']:
        SETTINGS.parent.mkdir(parents=True, exist_ok=True)
        SETTINGS.write_bytes(data)
    elif SETTINGS.exists():
        SETTINGS.unlink()
    if SETTINGS.exists() != record['exists'] or (record['exists'] and SETTINGS.read_bytes() != data):
        raise RuntimeError('Exact settings restoration readback failed')
    record.update(state='restored', restoredUtc=datetime.now(timezone.utc).isoformat(), exactBytesOrAbsenceVerified=True)
    save(BACKUP, record)
    save(logs / 'settings-restoration.json', record)
    print('Exact original settings bytes/absence restored. No default-value restoration claim.')


def prepare(args, logs):
    dotnet = shutil.which('dotnet')
    if not dotnet:
        raise ValueError('Installed SDK required; this tool installs nothing')
    packs = Path(dotnet).resolve().parent / 'packs/Microsoft.NETCore.App.Ref'
    versions = sorted((p for p in packs.glob('8.*') if p.is_dir()), key=lambda p: tuple(map(int, p.name.split('.'))))
    if not versions:
        raise ValueError('Installed net8 reference pack required; no download permitted')
    pack = versions[-1].name
    contract = ASSETS.parent / 'Contract/bin/Debug/net8.0/Issue34.StaticContract.dll'
    if not no_links(contract).is_file():
        raise ValueError('Prepare Boundary.sln first; this tool never builds/modifies its existing projects')
    run = logs.name
    work = no_links(ART / 'work' / run)
    work.mkdir(parents=True)
    (ART / 'feed').mkdir(exist_ok=True)
    dlls = no_links(ART / 'dll')
    dlls.mkdir(exist_ok=True)
    marker = logs / 'business-execution.marker'
    report = {'preparationOnly': True, 'preparationId': run, 'status': 'preparing', 'commands': [], 'net8Pack': pack,
              'port': args.port, 'offlinePort': args.offline_port, 'cases': {}, 'sentinel': str(marker),
              'notClaimed': ['host actions', 'whole-machine offline', 'private authorization', 'actual decompiler generation failure']}
    env = os.environ.copy()
    env.update(DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', NUGET_PLUGIN_PATHS='',
               NUGET_CREDENTIALPROVIDERS_PATH='', NUGET_CERT_REVOCATION_MODE='offline',
               ANOTHERFSM_COMPLEX_EXECUTION_MARKER=str(marker))
    common = ['--nologo', '--disable-build-servers', '-p:OwnedNet8PackVersion=' + pack,
              '-p:NuGetAudit=false', '-p:RestoreIgnoreFailedSources=false', '-nodeReuse:false']

    def run_command(name, arguments):
        if marker.exists():
            raise RuntimeError('Instrumented business execution marker exists; never deleted')
        command = [dotnet, *map(str, arguments)]
        log = logs / (name + '.log')
        item = {'name': name, 'command': command, 'log': str(log)}
        report['commands'].append(item)
        save(logs / 'summary.json', report)
        with log.open('w', encoding='utf-8') as stream:
            process = subprocess.Popen(command, cwd=ASSETS, env=env, stdout=stream, stderr=subprocess.STDOUT,
                                       start_new_session=os.name != 'nt')
            item['pid'] = process.pid
            try:
                item['exitCode'] = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                if os.name == 'nt':
                    subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=stream,
                                   stderr=subprocess.STDOUT, timeout=15, check=False)
                else:
                    import signal
                    os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=15)
                raise RuntimeError('Owned preparation command timed out: ' + name)
        if item['exitCode'] != 0 or marker.exists():
            raise RuntimeError('Preparation failed or sentinel present: ' + str(log))
        return log

    try:
        run_command('metadata-tool-build', ['build', ASSETS / 'MetadataTool/MetadataTool.csproj', '--configuration', 'Release',
                                           '--artifacts-path', work / 'tool', *common])
        tool = work / 'tool/bin/MetadataTool/release/MetadataTool.dll'
        for name in CASES:
            owned = no_links(work / name)
            owned.mkdir()
            source_root = ASSETS / 'candidates' if name == 'mismatch' else Path('/anotherfsm-owned') / name
            source_link = owned / 'sourcelink.json'
            mapping = {}
            if name in ('offline', 'unauthorized'):
                port = args.offline_port if name == 'offline' else args.port
                mapping = {f'/anotherfsm-owned/{name}/*': f'http://127.0.0.1:{port}/{name}/*'}
            save(source_link, {'documents': mapping})
            constant = owned / 'Candidate.cs'
            constant.write_text('internal static class OwnedCandidate { public const string Path = ' +
                                json.dumps((ASSETS / 'candidates/mismatch.cs').as_posix()) + '; }\n', encoding='utf-8')
            options = ['build', ASSETS / 'Library/SourceNodes.csproj', '--configuration', 'Release',
                       '--artifacts-path', owned / 'build', *common, '-p:SourceCase=' + name,
                       '-p:OwnedContract=' + contract.as_posix(), '-p:OwnedSourceLink=' + source_link.as_posix(),
                       '-p:OwnedCandidateConstant=' + constant.as_posix(), '-p:OwnedDocumentRoot=' + source_root.as_posix()]
            if name == 'nosymbols':
                options += ['-p:DebugType=None', '-p:DebugSymbols=false']
            run_command(name + '-build', options)
            built = owned / 'build/bin/SourceNodes/release'
            stem = 'Issue34.Source.' + name
            shutil.copyfile(no_links(built / (stem + '.dll')), no_links(dlls / (stem + '.dll')))
            pdb = no_links(dlls / (stem + '.pdb'))
            if pdb.exists():
                pdb.unlink()
            if name not in ('nosymbols', 'broken'):
                shutil.copyfile(no_links(built / (stem + '.pdb')), pdb)
            if name == 'broken':
                patch_log = run_command('broken-patch', [tool, 'patch-broken', ASSETS, name])
                report['brokenPatch'] = json.loads(patch_log.read_text(encoding='utf-8-sig'))
            inspected = run_command(name + '-metadata-inspect', [tool, 'inspect', ASSETS, name])
            evidence = json.loads(inspected.read_text(encoding='utf-8-sig'))
            if not evidence['metadataReadable'] or evidence['embeddedSource']:
                raise RuntimeError('Bad metadata preparation or unexpected embedded source')
            if name in ('offline', 'unauthorized'):
                links = [json.loads(value).get('documents', {}) for value in evidence['sourceLinks']]
                if links != [mapping] or evidence['pdbIdentityMatches'] is not True:
                    raise RuntimeError('Exact loopback-only Source Link / matching portable PDB not established')
            elif any(json.loads(value).get('documents', {}) for value in evidence['sourceLinks']):
                raise RuntimeError('Unexpected source mapping in non-Source-Link fixture')
            if name == 'mismatch' and not any(d['ownedCandidate'] and d['candidateChecksumMatches'] is False for d in evidence['documents']):
                raise RuntimeError('Actual PDB checksum mismatch not established')
            if name == 'nosymbols' and (evidence['pdbPresent'] or any(d['kind'] == 'CodeView' for d in evidence['debugEntries'])):
                raise RuntimeError('No-symbols fixture still exposes CodeView/PDB')
            report['cases'][name] = evidence
        report.update(status='prepared', sentinelAbsent=not marker.exists(), preparedUtc=datetime.now(timezone.utc).isoformat())
        save(ART / 'prepared.json', report)
        print('Prepared owned source cases only. Main must import SourceCases/Host.items.props into BoundaryHost.csproj.')
    finally:
        save(logs / 'summary.json', report)


def serve(args, logs):
    prepared = json.loads(no_links(ART / 'prepared.json').read_text(encoding='utf-8'))
    if prepared.get('status') != 'prepared':
        raise ValueError('Prepare the controlled assets before starting their source service')
    preparation_id = prepared['preparationId']
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            status = 200 if self.path == '/health' else 401 if self.path.startswith('/unauthorized/') else 404
            self.send_response(status)
            if status == 401:
                self.send_header('WWW-Authenticate', 'Basic realm="AnotherFSM-owned-fixture-only"')
            self.send_header('Content-Type', 'text/plain')
            self.send_header('X-AnotherFSM-Source-Fixture', preparation_id)
            self.end_headers()
            self.wfile.write(b'owned source-service availability fixture; no source bytes\n')
            with (logs / 'requests.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps({'utc': datetime.now(timezone.utc).isoformat(), 'status': status,
                                         'route': 'health' if self.path == '/health' else 'owned-401' if status == 401 else 'not-found',
                                         'authorizationReceived': 'Authorization' in self.headers}) + '\n')
        def log_message(self, *unused):
            pass
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    save(logs / 'server.json', {'pid': os.getpid(), 'host': '127.0.0.1', 'port': args.port,
                               'preparationId': preparation_id,
                               'scope': 'owned local HTTP401 only, not a real private-feed authorization test'})
    print('Loopback 401 server PID=' + str(os.getpid()) + ' logs=' + str(logs), flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('prepare', 'serve', 'backup-settings', 'restore-settings'))
    parser.add_argument('--port', type=int, default=38434)
    parser.add_argument('--offline-port', type=int, default=38435)
    parser.add_argument('--timeout', type=int, default=110)
    parser.add_argument('--confirm-sandbox-closed', action='store_true')
    parser.add_argument('--backup-id')
    args = parser.parse_args()
    if not 15 <= args.timeout <= 120 or not 1024 <= args.port <= 65535 or not 1024 <= args.offline_port <= 65535 or args.port == args.offline_port:
        parser.error('timeout 15..120; distinct unprivileged loopback ports required')
    logs = no_links(LOGS / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ-') + uuid.uuid4().hex[:8]))
    logs.mkdir(parents=True)
    no_links(ART).mkdir(parents=True, exist_ok=True)
    try:
        {'prepare': prepare, 'serve': serve, 'backup-settings': backup_settings, 'restore-settings': restore_settings}[args.action](args, logs)
    except Exception as error:
        (logs / 'error.txt').write_text(str(error), encoding='utf-8')
        print('ERROR: ' + str(error) + '; logs=' + str(logs), file=sys.stderr)
        return 1
    print('Logs: ' + str(logs))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
