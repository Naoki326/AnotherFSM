"""Public UI diagnostic journal contract, compiled without Rider SDK dependencies."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


class UiDiagnosticsTests(unittest.TestCase):
    def test_bounded_immutable_request_stage_and_reason_records(self):
        javac = shutil.which("javac") or "C:/Users/Naoki/AppData/Local/Programs/Rider/jbr/bin/javac.exe"
        java = str(Path(javac).with_name("java.exe" if str(javac).endswith(".exe") else "java"))
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "DiagnosticFacts.java"
            source.write_text('''import anotherfsm.UiDiagnostics;
public class DiagnosticFacts {
 public static void main(String[] a) {
  var journal=new UiDiagnostics(2);
  String id="00000000-0000-4000-8000-000000000001";
  journal.record(id,UiDiagnostics.Stage.RESOLVE,UiDiagnostics.Cause.SUBMITTED,1,3,3);
  var old=journal.snapshot();
  journal.record(id,UiDiagnostics.Stage.NAVIGATE_TYPE,UiDiagnostics.Cause.SUBMITTED,1,3,3);
  journal.record(id,UiDiagnostics.Stage.NAVIGATE_TYPE,UiDiagnostics.Cause.DOCUMENT_CHANGED,2,3,4);
  if(old.size()!=1||journal.snapshot().size()!=2)throw new AssertionError("bounded immutable snapshot");
  var last=journal.snapshot().get(1);
  if(last.sequence()!=3||last.stage()!=UiDiagnostics.Stage.NAVIGATE_TYPE||last.cause()!=UiDiagnostics.Cause.DOCUMENT_CHANGED
      ||last.documentVersion()!=2||last.resultEpoch()!=3||last.currentEpoch()!=4||!last.requestId().equals(id))
      throw new AssertionError("actual request/stage/cause/state must survive");
  try {old.clear();throw new AssertionError("mutable snapshot");}catch(UnsupportedOperationException expected){}
  journal.record("untrusted request text",UiDiagnostics.Stage.RESOLVE,UiDiagnostics.Cause.SUBMITTED,0,-1,-1);
  if(!journal.snapshot().get(1).requestId().equals("unavailable"))throw new AssertionError("never retain arbitrary request input");
 }
}''', encoding="utf-8")
            compiled = subprocess.run([javac, "-J-Duser.language=en", "-encoding", "UTF-8", "--release", "17", "-d", str(root),
                                       str(ROOT / "rider/frontend/src/anotherfsm/UiDiagnostics.java"), str(source)],
                                      text=True, errors="replace", capture_output=True, timeout=20)
            self.assertEqual(compiled.returncode, 0, (compiled.stdout or "") + (compiled.stderr or ""))
            result = subprocess.run([java, "-cp", str(root), "DiagnosticFacts"], text=True, errors="replace", capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 0, (result.stdout or "") + (result.stderr or ""))
