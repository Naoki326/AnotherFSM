import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from zipfile import ZipFile


SCRIPT = Path(__file__).resolve().parents[1] / "release.py"
spec = importlib.util.spec_from_file_location("release", SCRIPT)
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def test_stable_and_prerelease_tags(self):
        for tag, expected in [
            ("v2.0.0", ("2.0.0", False)),
            ("v0.1.0", ("0.1.0", False)),
            ("v2.0.0-beta.1", ("2.0.0-beta.1", True)),
            ("v2.0.0-rc.0", ("2.0.0-rc.0", True)),
            ("v2.0.0-beta-1", ("2.0.0-beta-1", True)),
        ]:
            with self.subTest(tag=tag):
                self.assertEqual(release.parse_tag(tag), expected)

    def test_rejects_invalid_or_ambiguous_tags(self):
        for tag in [
            "", "2.0.0", "v2.0", "v02.0.0", "v2.00.0", "v2.0.00",
            "v2.0.0.1", "v2.0.0-", "v2.0.0-beta..1", "v2.0.0-01",
            "v2.0.0-beta.01", "v2.0.0+build.1", "v2.0.0\n",
            "v2.0.0;echo unsafe", "v2.0.0-测试",
        ]:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.parse_tag(tag)

    def create_packages(self, directory, version="2.0.0", change=None):
        schemas = ["2013/05", "2011/10", "2012/06"]
        for (name, assembly), schema in zip(release.PACKAGE_CONTENTS.items(), schemas):
            package_id, package_version, path, contents = name, version, assembly, b"test DLL"
            if change:
                package_id, package_version, path, contents = change(name, version, assembly)
            with ZipFile(directory / f"{name}.{version}.nupkg", "w") as archive:
                archive.writestr(f"{name}.nuspec", (
                    f'<package xmlns="http://schemas.microsoft.com/packaging/{schema}/nuspec.xsd">'
                    f"<metadata><id>{package_id}</id><version>{package_version}</version></metadata></package>"
                ))
                archive.writestr(path, contents)

    def test_verifies_all_three_stable_and_prerelease_packages(self):
        for version in ["2.0.0", "2.0.0-beta.1"]:
            with self.subTest(version=version), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                self.create_packages(directory, version)
                release.verify_packages(f"v{version}", directory)

    def test_rejects_missing_or_extra_packages(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            with self.assertRaisesRegex(ValueError, "Package set mismatch"):
                release.verify_packages("v2.0.0", directory)
            self.create_packages(directory)
            (directory / "unexpected.nupkg").write_bytes(b"unexpected")
            with self.assertRaisesRegex(ValueError, "Package set mismatch"):
                release.verify_packages("v2.0.0", directory)

    def test_rejects_wrong_id_version_path_and_empty_assembly(self):
        mutations = [
            lambda name, version, path: ("Wrong.Id", version, path, b"DLL"),
            lambda name, version, path: (name, "1.0.0", path, b"DLL"),
            lambda name, version, path: (name, version, "wrong.dll", b"DLL"),
            lambda name, version, path: (name, version, path, b""),
        ]
        for mutate in mutations:
            with self.subTest(mutate=mutate), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                self.create_packages(directory, change=mutate)
                with self.assertRaises(ValueError):
                    release.verify_packages("v2.0.0", directory)

    def test_cli_emits_outputs_and_invalid_tag_emits_no_outputs(self):
        result = subprocess.run([sys.executable, str(SCRIPT), "version", "v2.0.0-beta.1"],
                                capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "version=2.0.0-beta.1\nprerelease=true\n")
        result = subprocess.run([sys.executable, str(SCRIPT), "version", "v2.0"],
                                capture_output=True, text=True, timeout=15)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "")


if __name__ == "__main__":
    unittest.main()
