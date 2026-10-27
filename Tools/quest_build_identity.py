"""Validate the built APK independently of installed/profile/wearer evidence."""
import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def runtime_input(name):
    return name.startswith("Assets/StreamingAssets/ResearchReaderDemo/") or name == "Assets/StreamingAssets/ResearchReaderDemo.meta" or (
            name.startswith("Assets/VRMaintenanceResearch/ResearchFlow/")
            and "/Editor/" not in name and "/Tests/" not in name
            and Path(name).suffix.lower() not in {".md", ".ps1", ".txt"}) or name in {
                "Assets/VRMaintenanceResearch/HandsLabQuest3/Runtime/HandsLabLogger.cs",
        "Assets/VRMaintenanceResearch/HandsLabQuest3/Runtime/HandsLabLogger.cs.meta",
        "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity",
                "Packages/manifest.json", "Packages/packages-lock.json",
                "ProjectSettings/ProjectVersion.txt", "ProjectSettings/QualitySettings.asset"}


def inspect(build_result, source_root=None, source_manifest=None, expected_sha=None):
    result = json.loads(Path(build_result).read_text(encoding="utf-8-sig"))
    if result.get("passed") is not True or result.get("result") != "Succeeded" or result.get("totalErrors") != 0:
        raise ValueError("build result is not a successful zero-error build")
    expected = result.get("sha256", "")
    if not isinstance(expected, str) or not re.fullmatch("[0-9a-f]{64}", expected):
        raise ValueError("build result SHA256 is missing or invalid")
    apk = Path(result["output"])
    if not apk.is_file() or apk.is_symlink() or apk.stat().st_size != result.get("sizeBytes"):
        raise ValueError("built APK is missing, non-regular, or has wrong size")
    if sha256(apk) != expected:
        raise ValueError("built APK SHA256 mismatch")
    if expected_sha and expected_sha.lower() != expected:
        raise ValueError("EXPECTED_SHA disagrees with verified build result")
    identity = {"build_result": str(Path(build_result).resolve()), "apk": str(apk.resolve()),
                "apk_sha256": expected, "source_state": "SOURCE_NOT_BOUND",
                "source_manifest": None, "source_deltas": [], "unbound_asset_count": None,
                "source_coverage": "hash-listed runtime inputs plus added runtime C#; unlisted media/assets not bound",
                "not_wearer_acceptance": True}
    if source_manifest:
        root = Path(source_root).resolve()
        manifest = json.loads(Path(source_manifest).read_text(encoding="utf-8-sig"))
        if not isinstance(manifest, dict):
            raise ValueError("source manifest must be a path-to-hash object")
        expected_paths = {name for name in manifest if runtime_input(name)}
        if not expected_paths:
            raise ValueError("source manifest has no scoped runtime inputs")
        actual_paths = {str(path.relative_to(root)).replace("\\", "/")
                        for folder in (root / "Assets/VRMaintenanceResearch/ResearchFlow", root / "Assets/StreamingAssets/ResearchReaderDemo")
                        for path in folder.rglob("*")
                        if path.is_file() and runtime_input(str(path.relative_to(root)).replace("\\", "/"))}
        reader_meta = "Assets/StreamingAssets/ResearchReaderDemo.meta"
        if (root / reader_meta).is_file():
            actual_paths.add(reader_meta)
        covered_actual = {name for name in actual_paths if name in expected_paths or name.endswith(".cs")}
        identity["unbound_asset_count"] = len(actual_paths - covered_actual)
        actual_paths = covered_actual
        actual_paths.update(name for name in expected_paths if not name.startswith("Assets/VRMaintenanceResearch/ResearchFlow/"))
        for name in sorted(expected_paths | actual_paths):
            raw_path = root / name
            path = raw_path.resolve()
            if not path.is_relative_to(root):
                raise ValueError("source manifest path escapes project root")
            record = manifest.get(name)
            if record is not None and not isinstance(record, dict):
                raise ValueError("source manifest record must contain bytes and SHA256")
            if record is None or not path.is_file() or raw_path.is_symlink() or path.stat().st_size != record.get("bytes") or sha256(path) != record.get("sha256"):
                identity["source_deltas"].append(name)
        identity.update(source_state="SOURCE_UNBUILT" if identity["source_deltas"] else "SCOPED_SOURCE_BUILD_MATCH",
                        source_manifest=str(Path(source_manifest).resolve()))
    return identity


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("build_result")
    parser.add_argument("--source-root")
    parser.add_argument("--source-manifest", default=os.environ.get("QUEST_BUILD_SOURCE_MANIFEST"))
    parser.add_argument("--expected-sha", default=os.environ.get("EXPECTED_SHA"))
    args = parser.parse_args()
    try:
        if args.source_manifest and not args.source_root:
            raise ValueError("source manifest requires explicit source root")
        identity = inspect(args.build_result, args.source_root, args.source_manifest, args.expected_sha)
        print(json.dumps(identity, separators=(",", ":")))
        if identity["source_state"] == "SOURCE_UNBUILT":
            print("REFUSED: current scoped runtime source differs from built APK; build/install identity is not current source acceptance", file=sys.stderr)
            sys.exit(3)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("BUILD_IDENTITY_REFUSED: " + str(error), file=sys.stderr)
        sys.exit(3)
