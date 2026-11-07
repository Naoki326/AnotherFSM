"""Supervise an already prepared, fixed owned sandbox. Never build/prepare/restore/kill.

Public seam: run_session(..., session_host=SessionHost()). An offline adapter need
only implement launch and identity; it may reuse the file-based outputs/request_exit.
Identities are PID + Windows creation FILETIME (opaque integers to the supervisor).
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import sys
import time
import uuid

sys.dont_write_bytecode = True

# These are existing reporters, not a new test/case partition. Unrelated reports
# cannot satisfy this suite. No report's `partial` status is a whole-AC pass.
REQUIRED_REPORTS = {
    'legacy': ('host-acceptance.json', 'dsl-host-acceptance.json',
               'host-reconnect.json', 'sdk-graph-probe.json', 'sdk-search-scope.json'),
    'complex': ('complex-host-acceptance.json', 'complex-sdk-probe.json'),
    'boundary': ('complex-boundary-acceptance.json',),
    'source': ('source-boundary-acceptance.json',),
}
TERMINAL = {'passed', 'failed', 'partial', 'pendingVerification', 'verificationPending'}
PRIMARY_REPORT = {suite: names[0] for suite, names in REQUIRED_REPORTS.items()}


def _read(path):
    # Windows virus scanners/readers can briefly deny an atomic replace/open.
    # Missing, truncated, or temporarily inaccessible data is never positive proof.
    try:
        value = json.loads(path.read_text(encoding='utf-8'))
        return value if isinstance(value, dict) else None
    except (OSError, ValueError):
        return None


def _write(path, value):
    from build import no_links
    no_links(path)
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        temporary.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
        deadline = time.monotonic() + .5
        while True:
            try:
                temporary.replace(path)
                break
            except PermissionError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(.01)
    finally:
        temporary.unlink(missing_ok=True)


class SessionHost:
    """Native Windows adapter. No process termination or host-settings operations.

    launch returns the persisted sandbox launch record, identity returns live
    creation FILETIME or None (inspection denial must raise, not mean 'stopped').
    outputs returns current JSON objects keyed by filename; transient IO is absent.
    request_exit only writes an identity-bound request; the observer calls exit.
    """
    def __init__(self, *, heartbeat_timeout=5.0, cleanup_timeout=15.0, poll_interval=.1):
        self.heartbeat_timeout = heartbeat_timeout
        self.cleanup_timeout = cleanup_timeout
        self.poll_interval = poll_interval

    def launch(self, suite, logs, run_id):
        from build import DEFAULT_RIDER, host
        from sandbox import launch_sandbox
        home, _, launch = host(Path(os.environ.get('RIDER_HOME', str(DEFAULT_RIDER))))
        return launch_sandbox(home, launch, logs, acceptance=True,
                              complex_contexts=suite == 'complex',
                              boundary_contexts=suite in ('boundary', 'source'),
                              source_boundaries=suite == 'source', run_id=run_id)

    def identity(self, pid):
        from sandbox import windows_process
        return windows_process(pid)

    def request_exit(self, logs, run_id, frontend):
        if self.identity(frontend['pid']) != frontend['creationFileTime']:
            raise ValueError('Frontend identity changed; refusing exit request')
        _write(logs / 'acceptance/exit-request.json', {
            'runId': run_id, 'frontendPid': frontend['pid'],
            'creationFileTime': frontend['creationFileTime'], 'action': 'exit',
        })

    def outputs(self, logs):
        from build import no_links
        from sandbox import BOX
        paths = [logs / 'process.json', BOX / 'prepared.json']
        scan_complete = False
        try:
            evidence = no_links(logs / 'acceptance')
            if evidence.is_dir():
                paths.extend(path for path in evidence.iterdir() if path.suffix == '.json')
                scan_complete = True
        except OSError:
            pass
        result = {'identity-scan.json': {'complete': scan_complete}}
        for path in paths:
            try:
                value = _read(no_links(path))
                if value is not None:
                    result[path.name] = value
                elif path.name.startswith('backend-identity-') or path.name == 'native-observation.json':
                    result[path.name] = {'identityReadPending': True}
            except OSError:
                if path.name.startswith('backend-identity-') or path.name == 'native-observation.json':
                    result[path.name] = {'identityReadPending': True}
        return result


def _identity(pid, created):
    if (isinstance(pid, int) and not isinstance(pid, bool) and pid > 0 and
            isinstance(created, int) and not isinstance(created, bool) and created > 0):
        return {'pid': pid, 'creationFileTime': created}
    return None


def _summary(suite, outputs):
    reports, coverage, pending = {}, [], []
    for name in REQUIRED_REPORTS[suite]:
        report = outputs.get(name)
        if report is None:
            pending.append(name + ': missing or temporarily unreadable')
            continue
        status = report.get('status')
        reports[name] = status
        for item in report.get('coverage', []):
            if isinstance(item, dict):
                coverage.append({'report': name, **item})
                if item.get('status') != 'passed':
                    pending.append(name + ': ' + str(item.get('name')) + ' (' + str(item.get('status')) + ')')
        for item in report.get('pendingVerification', []):
            pending.append(name + ': ' + str(item))
        if status != 'passed':
            pending.append(name + ': ' + str(status))
    failed = any(status == 'failed' for status in reports.values()) or any(
        item.get('status') == 'failed' for item in coverage)
    complete = len(reports) == len(REQUIRED_REPORTS[suite]) and all(s in TERMINAL for s in reports.values())
    return dict(reports=reports, actualCoverage=coverage, pendingVerification=pending,
                allRequiredPassed=complete and not pending and not failed), failed, complete


def run_session(suite, logs: Path, startup_timeout, total_timeout, *, session_host=None):
    """Return plain JSON-compatible evidence, including cleanup confirmation.

    status: notStarted / heartbeatStopped / assertionFailed /
    verificationPending / succeeded / sessionError. cleanupBlocked is independent
    of the test outcome. Caller MUST retain backups when hostsStopped is false;
    even true is only permission to perform its own guarded restoration, not a
    claim that restoration happened. This function never touches those backups.
    """
    adapter = session_host if session_host is not None else SessionHost()
    logs = Path(logs)
    run_id = str(uuid.uuid4())
    result = dict(runId=run_id, suite=suite, artifactHash=None, status='notStarted',
                  allRequiredPassed=False, suiteStarted=False, actualCoverage=[], pendingVerification=[],
                  hostsStopped=False, hostStopped=False, cleanupBlocked=True, frontendIdentity=None,
                  backendIdentities=[], backendIdentityPending=[], cleanupErrors=[])
    record = None
    launched = False
    cleanup_interrupt = None
    backend = {}
    unresolved_backend = set()
    outputs = {}

    def sample():
        nonlocal outputs, record
        outputs = adapter.outputs(logs)
        if record is None:
            candidate = outputs.get('process.json', {})
            if candidate.get('runId') == run_id:
                record = candidate
        if record and record.get('runId') == run_id:
            result['artifactHash'] = record.get('zipSha256', result['artifactHash'])
            result['frontendIdentity'] = _identity(record.get('windowsPid'), record.get('creationFileTime'))
        scan = outputs.get('identity-scan.json')
        if scan is not None:
            if scan.get('complete'):
                unresolved_backend.discard(('observer-directory', None))
            else:
                unresolved_backend.add(('observer-directory', None))
        for name, value in outputs.items():
            if name != 'native-observation.json' and not name.startswith('backend-identity-'):
                continue
            if value.get('identityReadPending'):
                unresolved_backend.add((name, None))
                continue
            if value.get('runId') != run_id:
                unresolved_backend.discard((name, None))
                continue
            item = _identity(value.get('backendPid'), value.get('backendCreationFileTime'))
            if item:
                backend[(item['pid'], item['creationFileTime'])] = item
                unresolved_backend.discard((name, item['pid']))
                unresolved_backend.discard((name, None))
            else:
                pid = value.get('backendPid')
                unresolved_backend.add((name, pid if isinstance(pid, int) else None))
        result['backendIdentities'] = list(backend.values())
        result['backendIdentityPending'] = [{'file': name, 'pid': pid} for name, pid in unresolved_backend]
        if result['artifactHash'] is None:
            result['artifactHash'] = outputs.get('prepared.json', {}).get('zipSha256')

    try:
        logs.mkdir(parents=True, exist_ok=True)
        if suite not in REQUIRED_REPORTS:
            raise ValueError('Unknown suite: ' + str(suite))
        if startup_timeout <= 0 or total_timeout <= 0:
            raise ValueError('Timeouts must be positive')
        # Never consume stale reporters or requests from an earlier session.
        if (logs / 'acceptance').exists() or (logs / 'process.json').exists():
            raise ValueError('Session requires fresh logs (no acceptance/ or process.json)')
        sample()
        launched = True  # launch may fail AFTER creating and persisting the JVM PID.
        record = adapter.launch(suite, logs, run_id)
        sample()
        frontend = result['frontendIdentity']
        if not frontend:
            raise ValueError('Launch did not record a live frontend creation FILETIME')
        if adapter.identity(frontend['pid']) != frontend['creationFileTime']:
            raise ValueError('Launched frontend identity does not match')
        started = time.monotonic()
        heartbeat_at, sequence = None, None
        while True:
            sample()
            now = time.monotonic()
            heartbeat = outputs.get('lifecycle.json', {})
            if (heartbeat.get('runId') == run_id and heartbeat.get('suite') == suite and
                    heartbeat.get('frontendPid') == frontend['pid'] and
                    heartbeat.get('fixture') == record.get('fixture') and
                    isinstance(heartbeat.get('seq'), int) and heartbeat['seq'] > (sequence or 0)):
                sequence, heartbeat_at = heartbeat['seq'], now
            primary = outputs.get(PRIMARY_REPORT[suite], {})
            if heartbeat_at is not None and primary.get('status') in TERMINAL | {'running', 'waiting', 'cleaningUp'}:
                result['suiteStarted'] = True
            summary, failed, terminal = _summary(suite, outputs)
            result.update(summary)
            # Reports are accepted only after the independent owned lifecycle is seen.
            if heartbeat_at is not None:
                if failed:
                    result['status'] = 'assertionFailed'
                    break
                if now - heartbeat_at >= adapter.heartbeat_timeout:
                    result['allRequiredPassed'] = False
                    result['status'] = 'heartbeatStopped'
                    break
                if terminal:
                    result['status'] = 'succeeded' if summary['allRequiredPassed'] else 'verificationPending'
                    break
            else:
                result['allRequiredPassed'] = False
                result['pendingVerification'].append('Owned lifecycle heartbeat not observed')
                if now - started >= startup_timeout:
                    result['status'] = 'notStarted'
                    break
            if not result['suiteStarted'] and now - started >= startup_timeout:
                result['status'] = 'notStarted'
                result['allRequiredPassed'] = False
                result['pendingVerification'].append('Requested suite did not produce its primary startup report')
                break
            if now - started >= total_timeout:
                result['status'] = 'verificationPending' if heartbeat_at is not None else 'notStarted'
                result['pendingVerification'].append('Session total deadline reached; no inferred assertion failure')
                break
            time.sleep(adapter.poll_interval)
    except Exception as error:
        result['status'] = 'sessionError'
        result['error'] = str(error)
    finally:
        if launched:
            try:
                sample()
                frontend = result['frontendIdentity']
                if not frontend:
                    raise ValueError('Frontend identity unavailable; retain backup')
                current = adapter.identity(frontend['pid'])
                if current is not None:
                    if current != frontend['creationFileTime']:
                        raise ValueError('Frontend PID identity differs; no exit request sent')
                    adapter.request_exit(logs, run_id, frontend)
                deadline = time.monotonic() + adapter.cleanup_timeout
                stable = 0
                while True:
                    sample()  # Include ALL run-correlated backend identities, including late/restarted hosts.
                    identities = [frontend, *backend.values()]
                    live = False
                    for item in identities:
                        current = adapter.identity(item['pid'])
                        if current is not None:
                            if current != item['creationFileTime']:
                                raise ValueError('Recorded PID identity changed; cannot confirm cleanup')
                            live = True
                    # Absence of a backend report is NOT proof that no backend exists.
                    stable = stable + 1 if not live and backend and not unresolved_backend else 0
                    if stable >= 2:
                        result['hostsStopped'] = result['hostStopped'] = True
                        result['cleanupBlocked'] = False
                        break
                    if time.monotonic() >= deadline:
                        raise TimeoutError('Owned host shutdown not confirmed (missing observer identity or live process); retain backup')
                    time.sleep(adapter.poll_interval)
            except BaseException as error:
                result['hostsStopped'] = result['hostStopped'] = False
                result['cleanupBlocked'] = True
                result['cleanupErrors'].append(type(error).__name__ + ': ' + str(error))
                if not isinstance(error, Exception):
                    cleanup_interrupt = error
        else:
            # In particular, rejected/reused logs may belong to a still-running host.
            # No launch is NOT evidence that the caller's sandbox is stopped.
            result['cleanupErrors'].append('No owned launch identity; host shutdown not confirmed')
        result['restoration'] = 'Not performed; caller owns exact settings/source backup and server management'
        # Independent receipt is written even when BaseException (e.g. Ctrl-C)
        # propagates out of the session. Missing receipt is never permission to restore.
        receipt = {key: result[key] for key in ('runId', 'artifactHash', 'hostsStopped',
                   'cleanupBlocked', 'frontendIdentity', 'backendIdentities', 'backendIdentityPending', 'cleanupErrors')}
        for name, value in (('cleanup-receipt.json', receipt), ('session-result.json', result)):
            try:
                _write(logs / name, value)
            except (OSError, ValueError) as error:
                result.setdefault('receiptWriteErrors', []).append(name + ': ' + str(error))
        if cleanup_interrupt is not None:
            raise cleanup_interrupt
    return result
