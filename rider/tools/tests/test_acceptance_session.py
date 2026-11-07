"""Offline supervision contract: real Python processes, no IDE or business fixtures."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from acceptance_session import SessionHost, run_session


CHILD = r'''
import json, os, pathlib, subprocess, sys, time
sys.path.insert(0, sys.argv[1])
from sandbox import windows_process
out = pathlib.Path(sys.argv[2]); run_id, suite, mode, role = sys.argv[3:7]
def identity(pid):
    if os.name == 'nt': return windows_process(pid)
    try: return int(pathlib.Path('/proc/%s/stat' % pid).read_text().split()[21])
    except FileNotFoundError: return None
def put(name, value):
    p = out / name; tmp = p.with_suffix('.tmp')
    for _ in range(20):
        try:
            tmp.write_text(value if isinstance(value, str) else json.dumps(value)); tmp.replace(p); return
        except OSError: time.sleep(.005)
    raise RuntimeError('atomic output remained inaccessible: ' + name)
pid = os.getpid(); created = identity(pid)
if role.startswith('backend'):
    put('backend-identity-%s.json' % pid, dict(runId=run_id, backendPid=pid,
        backendCreationFileTime=None if role == 'backendIncomplete' else created))
else:
    backends = [] if mode == 'noBackend' else [subprocess.Popen([sys.executable, __file__, *sys.argv[1:6], 'backend'])]
    if mode in ('multiple', 'mixedIncomplete'):
        backends.append(subprocess.Popen([sys.executable, __file__, *sys.argv[1:6],
                                          'backendIncomplete' if mode == 'mixedIncomplete' else 'backend']))
started = time.monotonic(); seq = 0
while time.monotonic() - started < 2.5:
    if role == 'frontend' and mode != 'noHeartbeat' and not (mode == 'stalled' and seq > 0):
        seq += 1
        put('lifecycle.json', dict(runId='foreign' if mode == 'foreignHeartbeat' else run_id,
                                   suite=suite, frontendPid=pid, fixture='offline-fixture', seq=seq))
        if mode in ('failed', 'partial', 'partialDelayed', 'pending', 'passed', 'missingScope', 'unrelated', 'malformed'):
            status = {'failed':'failed','partial':'partial','partialDelayed':'partial','pending':'pendingVerification'}.get(mode,'passed')
            name = 'complex-host-acceptance.json' if mode == 'unrelated' else 'host-acceptance.json'
            put(name, '{' if mode == 'malformed' else dict(status=status, coverage=[dict(name='actual assertion', status='passed')],
                           pendingVerification=['unverified AC'] if mode == 'partial' else []))
            if mode not in ('failed', 'unrelated') and (mode != 'partialDelayed' or time.monotonic() - started >= .18):
                for name in ('dsl-host-acceptance.json','host-reconnect.json','sdk-graph-probe.json'):
                    put(name, dict(status='passed'))
                if mode != 'missingScope': put('sdk-search-scope.json', dict(status='passed'))
    try:
        request = json.loads((out / 'exit-request.json').read_text())
        if request['runId'] == run_id:
            if role.startswith('backend'):
                if mode != 'lingering': break
                time.sleep(.02); continue
            if request['frontendPid'] == pid and request['creationFileTime'] == created: break
    except (OSError, ValueError, KeyError): pass
    time.sleep(.02)
if role == 'frontend':
    for backend in backends: backend.wait(timeout=4)
'''


class PythonProcessHost(SessionHost):
    """Uses only the public adapter seam; children validate their own exit identity."""
    def __init__(self, mode, *, wrong_identity=False):
        super().__init__(heartbeat_timeout=.12, cleanup_timeout=.7, poll_interval=.02)
        self.mode = mode
        self.wrong_identity = wrong_identity
        self.process = None
        self.interrupted = False
        self.cleanup_started = False

    def request_exit(self, logs, run_id, frontend):
        super().request_exit(logs, run_id, frontend)
        self.cleanup_started = True

    def outputs(self, logs):
        values = super().outputs(logs)
        should_interrupt = ((self.mode == 'interrupt' and 'lifecycle.json' in values)
                            or (self.mode == 'cleanupInterrupt' and self.cleanup_started))
        if should_interrupt and not self.interrupted:
            self.interrupted = True
            raise KeyboardInterrupt('offline public adapter interruption')
        return values

    def launch(self, suite, logs, run_id):
        evidence = logs / 'acceptance'
        evidence.mkdir()
        script = logs / 'child.py'
        script.write_text(CHILD, encoding='utf-8')
        self.process = subprocess.Popen([sys.executable, '-B', str(script), str(Path(__file__).resolve().parents[1]),
                                         str(evidence), run_id, suite, self.mode, 'frontend'])
        created = self.identity(self.process.pid)
        record = dict(windowsPid=self.process.pid, creationFileTime=created + int(self.wrong_identity),
                      zipSha256='offline-artifact', fixture='offline-fixture', runId=run_id)
        if self.mode == 'launchError':
            (logs / 'process.json').write_text(json.dumps(record), encoding='utf-8')
            raise OSError('launch failed after persisting process identity')
        return record

    def identity(self, pid):
        if os.name == 'nt':
            return super().identity(pid)
        if self.process is not None and pid == self.process.pid and self.process.poll() is not None:
            return None
        try:
            fields = Path('/proc/%s/stat' % pid).read_text().split()
            return None if fields[2] == 'Z' else int(fields[21])
        except FileNotFoundError:
            return None

    def join(self):
        if self.process:
            self.process.wait(timeout=5)


class SessionTests(unittest.TestCase):
    def session(self, mode, *, wrong_identity=False, suite='legacy'):
        adapter = PythonProcessHost(mode, wrong_identity=wrong_identity)
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        # Join BEFORE removing the child's output directory (LIFO cleanups).
        self.addCleanup(adapter.join)
        logs = Path(temp.name)
        result = run_session(suite, logs, .35, .8, session_host=adapter)
        self.assertTrue(result['runId'])
        self.assertEqual('offline-artifact', result['artifactHash'])
        receipt = json.loads((logs / 'cleanup-receipt.json').read_text())
        self.assertEqual(result['hostsStopped'], receipt['hostsStopped'])
        self.assertEqual(result['hostStopped'], result['hostsStopped'])
        self.assertEqual(result['runId'], receipt['runId'])
        return result, logs, adapter

    def test_no_heartbeat_timeout_still_requests_exit_and_waits_for_both_processes(self):
        result, logs, adapter = self.session('noHeartbeat')
        self.assertEqual('notStarted', result['status'])
        self.assertTrue(result['hostStopped'])
        self.assertFalse(result['cleanupBlocked'])
        self.assertIsNotNone(adapter.process.poll())
        self.assertTrue(result['backendIdentities'])
        self.assertEqual(result['runId'], json.loads((logs / 'acceptance/exit-request.json').read_text())['runId'])

    def test_pid_with_different_creation_identity_is_never_asked_to_exit(self):
        result, logs, _ = self.session('noHeartbeat', wrong_identity=True)
        self.assertTrue(result['cleanupBlocked'])
        self.assertFalse(result['hostStopped'])
        self.assertFalse((logs / 'acceptance/exit-request.json').exists())

    def test_terminal_assertion_failure_still_stops_host(self):
        result, _, _ = self.session('failed')
        self.assertEqual('assertionFailed', result['status'])
        self.assertTrue(result['hostStopped'])

    def test_partial_and_pending_never_become_passed(self):
        for mode in ('partial', 'pending'):
            with self.subTest(mode=mode):
                result, _, _ = self.session(mode)
                self.assertEqual('verificationPending', result['status'])
                self.assertTrue(result['pendingVerification'])
                self.assertFalse(result['allRequiredPassed'])
                self.assertTrue(result['hostStopped'])

    def test_partial_report_does_not_stop_other_required_reporters_early(self):
        result, _, _ = self.session('partialDelayed')
        self.assertEqual('verificationPending', result['status'])
        self.assertEqual(5, len(result['reports']))
        self.assertTrue(result['hostsStopped'])

    def test_lifecycle_alone_does_not_prove_the_requested_suite_started(self):
        result, _, _ = self.session('noReport')
        self.assertEqual('notStarted', result['status'])
        self.assertFalse(result['allRequiredPassed'])
        self.assertTrue(result['hostsStopped'])

    def test_stopped_heartbeat_is_distinct_from_startup_timeout(self):
        result, _, _ = self.session('stalled')
        self.assertEqual('heartbeatStopped', result['status'])
        self.assertTrue(result['hostStopped'])

    def test_only_required_suite_reports_count(self):
        result, _, _ = self.session('unrelated')
        self.assertEqual('notStarted', result['status'])
        self.assertFalse(result['actualCoverage'])

    def test_legacy_scope_regression_cannot_be_silently_omitted(self):
        result, _, _ = self.session('missingScope')
        self.assertEqual('verificationPending', result['status'])
        self.assertFalse(result['allRequiredPassed'])
        self.assertTrue(result['hostsStopped'])

    def test_all_required_reports_passed(self):
        result, _, _ = self.session('passed')
        self.assertEqual('succeeded', result['status'])
        self.assertTrue(result['allRequiredPassed'])
        self.assertTrue(result['hostStopped'])

    def test_multiple_observed_backend_identities_all_exit(self):
        result, _, _ = self.session('multiple')
        self.assertEqual(2, len(result['backendIdentities']))
        self.assertTrue(result['hostsStopped'])

    def test_one_valid_backend_cannot_hide_an_incomplete_second_identity(self):
        result, _, _ = self.session('mixedIncomplete')
        self.assertEqual(1, len(result['backendIdentities']))
        self.assertTrue(result['backendIdentityPending'])
        self.assertFalse(result['hostsStopped'])
        self.assertTrue(result['cleanupBlocked'])

    def test_missing_backend_identity_never_authorizes_restore(self):
        result, _, _ = self.session('noBackend')
        self.assertFalse(result['hostsStopped'])
        self.assertTrue(result['cleanupBlocked'])

    def test_lingering_backend_blocks_restore_even_after_exit_request(self):
        result, logs, _ = self.session('lingering')
        self.assertTrue((logs / 'acceptance/exit-request.json').is_file())
        self.assertFalse(result['hostsStopped'])
        self.assertTrue(result['cleanupBlocked'])

    def test_foreign_heartbeat_does_not_establish_startup(self):
        result, _, _ = self.session('foreignHeartbeat')
        self.assertEqual('notStarted', result['status'])
        self.assertTrue(result['hostsStopped'])

    def test_launch_exception_after_persisting_pid_still_cleans_up(self):
        result, _, _ = self.session('launchError')
        self.assertEqual('sessionError', result['status'])
        self.assertTrue(result['hostsStopped'])

    def test_propagated_interrupt_writes_cleanup_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            adapter = PythonProcessHost('interrupt')
            try:
                with self.assertRaises(KeyboardInterrupt):
                    run_session('legacy', Path(directory), .35, .8, session_host=adapter)
                receipt = json.loads((Path(directory) / 'cleanup-receipt.json').read_text())
                self.assertTrue(receipt['hostsStopped'])
                self.assertFalse(receipt['cleanupBlocked'])
                self.assertEqual('offline-artifact', receipt['artifactHash'])
            finally:
                adapter.join()

    def test_cleanup_interrupt_also_writes_blocked_receipt_before_propagating(self):
        with tempfile.TemporaryDirectory() as directory:
            adapter = PythonProcessHost('cleanupInterrupt')
            try:
                with self.assertRaises(KeyboardInterrupt):
                    run_session('legacy', Path(directory), .35, .8, session_host=adapter)
                receipt = json.loads((Path(directory) / 'cleanup-receipt.json').read_text())
                self.assertFalse(receipt['hostsStopped'])
                self.assertTrue(receipt['cleanupBlocked'])
            finally:
                adapter.join()

    def test_reused_logs_never_claim_a_host_is_stopped(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / 'acceptance').mkdir()
            adapter = PythonProcessHost('noHeartbeat')
            result = run_session('legacy', Path(directory), .35, .8, session_host=adapter)
            self.assertIsNone(adapter.process)
            self.assertFalse(result['hostsStopped'])
            self.assertTrue(result['cleanupBlocked'])

    def test_unreadable_primary_report_never_proves_suite_startup(self):
        result, _, _ = self.session('malformed')
        self.assertEqual('notStarted', result['status'])
        self.assertFalse(result['allRequiredPassed'])
        self.assertTrue(result['hostStopped'])


if __name__ == '__main__':
    unittest.main()
