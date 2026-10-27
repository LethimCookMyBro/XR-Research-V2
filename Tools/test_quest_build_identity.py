import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from quest_build_identity import inspect


class BuildIdentityTests(unittest.TestCase):
    def test_hash_listed_streaming_assets_and_meta_change_is_unbuilt(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            apk = root / "candidate.apk"
            apk.write_bytes(b"same APK")
            result_path = root / "build_result.json"
            result_path.write_text(json.dumps({"passed": True, "result": "Succeeded", "totalErrors": 0,
                                               "output": str(apk), "sizeBytes": apk.stat().st_size,
                                               "sha256": hashlib.sha256(apk.read_bytes()).hexdigest()}), encoding="utf-8")
            names = ["Assets/VRMaintenanceResearch/ResearchFlow/UI/Reader.cs",
                     "Assets/StreamingAssets/ResearchReaderDemo/source.pdf",
                     "Assets/StreamingAssets/ResearchReaderDemo/test.mp4",
                     "Assets/StreamingAssets/ResearchReaderDemo/source.pdf.meta",
                     "Assets/StreamingAssets/ResearchReaderDemo/test.mp4.meta",
                     "Assets/StreamingAssets/ResearchReaderDemo.meta",
                "Assets/VRMaintenanceResearch/HandsLabQuest3/Runtime/HandsLabLogger.cs",
                "Assets/VRMaintenanceResearch/HandsLabQuest3/Runtime/HandsLabLogger.cs.meta"]
            files = {}
            for name in names:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"baseline")
                files[name] = {"bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
            manifest = root / "source-at-build.json"
            manifest.write_text(json.dumps(files), encoding="utf-8")
            self.assertEqual(inspect(result_path, root, manifest)["source_state"], "SCOPED_SOURCE_BUILD_MATCH")
            for name in names[1:]:
                with self.subTest(packaged_input=name):
                    path = root / name
                    path.write_bytes(b"modified")
                    observed = inspect(result_path, root, manifest)
                    self.assertEqual(observed["source_state"], "SOURCE_UNBUILT")
                    self.assertEqual(observed["source_deltas"], [name])
                    path.write_bytes(b"baseline")

    def test_verified_artifact_and_fail_closed_boundaries(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            apk = root / "candidate.apk"
            apk.write_bytes(b"same built APK input")
            digest = hashlib.sha256(apk.read_bytes()).hexdigest()
            result_path = root / "build_result.json"
            result = {"passed": True, "result": "Succeeded", "totalErrors": 0,
                      "output": str(apk), "sizeBytes": apk.stat().st_size, "sha256": digest}
            result_path.write_text(json.dumps(result), encoding="utf-8")
            self.assertEqual(inspect(result_path)["apk_sha256"], digest)
            self.assertEqual(inspect(result_path)["source_state"], "SOURCE_NOT_BOUND")
            with self.assertRaisesRegex(ValueError, "EXPECTED_SHA disagrees"):
                inspect(result_path, expected_sha="0" * 64)
            apk.write_bytes(b"x" * result["sizeBytes"])
            with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
                inspect(result_path)
            apk.unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                inspect(result_path)
            apk.write_bytes(b"same built APK input")
            result["passed"] = False
            result_path.write_text(json.dumps(result), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "not a successful"):
                inspect(result_path)
            result["passed"] = True
            result_path.write_text(json.dumps(result), encoding="utf-8")
            with self.assertRaises(FileNotFoundError):
                inspect(root / "missing.json")
            name = "Assets/VRMaintenanceResearch/ResearchFlow/UI/Reader.cs"
            source = root / name
            source.parent.mkdir(parents=True)
            source.write_bytes(b"baseline runtime")
            manifest = root / "source-at-build.json"
            manifest.write_text(json.dumps({name: {"bytes": source.stat().st_size,
                                                   "sha256": hashlib.sha256(source.read_bytes()).hexdigest()}}), encoding="utf-8")
            self.assertEqual(inspect(result_path, root, manifest)["source_state"], "SCOPED_SOURCE_BUILD_MATCH")
            source.write_bytes(b"changed runtime")
            self.assertEqual(inspect(result_path, root, manifest)["source_deltas"], [name])
            refused = subprocess.run([sys.executable, str(Path(__file__).with_name("quest_build_identity.py")),
                                      str(result_path), "--source-root", str(root), "--source-manifest", str(manifest)],
                                     capture_output=True, text=True)
            self.assertEqual(refused.returncode, 3)
            self.assertEqual(json.loads(refused.stdout)["source_state"], "SOURCE_UNBUILT")
            self.assertIn("current scoped runtime source differs", refused.stderr)
            source.write_bytes(b"baseline runtime")
            new_source = source.with_name("NewReader.cs")
            new_source.write_bytes(b"unbuilt addition")
            self.assertEqual(inspect(result_path, root, manifest)["source_state"], "SOURCE_UNBUILT")
            new_source.unlink()
            editor = root / "Assets/VRMaintenanceResearch/ResearchFlow/Editor/EditorOnly.cs"
            editor.parent.mkdir()
            editor.write_bytes(b"editor check, not runtime")
            self.assertEqual(inspect(result_path, root, manifest)["source_state"], "SCOPED_SOURCE_BUILD_MATCH")
            source.with_name("ExistingUnlistedImage.png").write_bytes(b"not present in the scoped historical map")
            observed = inspect(result_path, root, manifest)
            self.assertEqual(observed["source_state"], "SCOPED_SOURCE_BUILD_MATCH")
            self.assertEqual(observed["unbound_asset_count"], 1)
            escape = "Assets/VRMaintenanceResearch/ResearchFlow/../../../../outside.cs"
            manifest.write_text(json.dumps({escape: {"bytes": 0, "sha256": "0" * 64}}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "escapes project root"):
                inspect(result_path, root, manifest)


if __name__ == "__main__":
    unittest.main()
