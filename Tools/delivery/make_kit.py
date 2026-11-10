"""Assemble the delivery kit from a finished Quest build.

  python make_kit.py --build-dir <Logs/QuestFinalQA/build> --out <new kit folder> [--docs extra.md ...]

Writes apk/ (APK + BUILD_IDENTITY.json), scripts/, tools/, docs/, SHA256SUMS.txt, and a sibling <out>_PRIVATE_signing/ with the
signing keystore (keep it off public Git: an APK signed with a different key cannot update the installed app without data loss).
Refuses an existing output folder and an APK whose hash differs from build_result.json.
"""
import argparse, hashlib, json, shutil, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
UNITY_ANDROID = Path(r"C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer")
KIT_TOOLS = ["research_export.py", "quest_profile.py", "research_cloud_sync.py", "s1_session_report.py",
             "drive.config.example.json", "research-questionnaire.schema.json"]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def signer_digest(apk):
    tools = sorted((UNITY_ANDROID / "SDK" / "build-tools").glob("*/lib/apksigner.jar"))
    if not tools:
        return None, "apksigner.jar not found"
    java = UNITY_ANDROID / "OpenJDK" / "bin" / "java.exe"
    out = subprocess.run([str(java), "-jar", str(tools[-1]), "verify", "--print-certs", "-v", str(apk)], capture_output=True, text=True)
    digest = next((l.split(":", 1)[1].strip() for l in out.stdout.splitlines() if "certificate SHA-256 digest" in l), None)
    verified = "Verifies" in out.stdout
    return digest, ("verified" if verified else out.stdout[-400:] + out.stderr[-400:])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--build-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--docs", nargs="*", default=[])
    a = ap.parse_args()
    build, out = Path(a.build_dir), Path(a.out)
    if out.exists():
        sys.exit("output folder exists; use a new one")
    result = json.loads((build / "build_result.json").read_text(encoding="utf-8-sig"))
    config = json.loads((build / "build_configuration.json").read_text(encoding="utf-8-sig"))
    if result.get("passed") is not True or result.get("totalErrors") != 0:
        sys.exit("build_result.json is not a passed zero-error build")
    apk = Path(result["output"])
    apk_sha = sha256(apk)
    if apk_sha != result["sha256"]:
        sys.exit("APK hash differs from build_result.json")
    digest, signature = signer_digest(apk)
    (out / "apk").mkdir(parents=True)
    shutil.copy2(apk, out / "apk" / apk.name)
    identity = dict(build_id=apk.stem, package=config["applicationIdentifier"], product_name=config["productName"],
                    version_name=config["versionName"], version_code=config["versionCode"], flavor=config.get("flavor"),
                    development_build=config.get("development"), unity=config.get("unityVersion"), target_sdk=config.get("targetSdk"),
                    apk_file=apk.name, apk_sha256=apk_sha, apk_size_bytes=apk.stat().st_size,
                    signing_certificate_sha256=digest, signature_check=signature)
    (out / "apk" / "BUILD_IDENTITY.json").write_text(json.dumps(identity, indent=1, ensure_ascii=False), encoding="utf-8")
    (out / "scripts").mkdir()
    for f in HERE.glob("*.ps1"):
        shutil.copy2(f, out / "scripts" / f.name)
    (out / "tools").mkdir()
    for name in KIT_TOOLS:
        shutil.copy2(TOOLS / name, out / "tools" / name)
    shutil.copytree(TOOLS / "s1_cloud_relay", out / "tools" / "s1_cloud_relay", ignore=shutil.ignore_patterns("__pycache__", "*.pyc", ".env*"))
    (out / "docs").mkdir()
    for doc in [HERE / "README_TH.md", *map(Path, a.docs)]:
        shutil.copy2(doc, out / "docs" / doc.name)
    sums = [f"{sha256(p)}  {p.relative_to(out).as_posix()}" for p in sorted(out.rglob("*")) if p.is_file()]
    (out / "SHA256SUMS.txt").write_text("\n".join(sums) + "\n", encoding="utf-8", newline="\n")  # LF so `sha256sum -c` works everywhere
    private = out.parent / (out.name + "_PRIVATE_signing")
    private.mkdir()
    keystore = Path.home() / ".android" / "debug.keystore"
    shutil.copy2(keystore, private / "debug.keystore")
    (private / "README.txt").write_text(
        "Unity signs this build with the Android debug keystore of the build PC (useCustomKeystore=false).\n"
        "Copy debug.keystore to %USERPROFILE%\\.android\\ on a new build PC so new APKs can update the installed app.\n"
        f"Certificate SHA-256: {digest}\nDo not publish this folder.\n"
        "enroll_key.json (if present) is the cloud install key built into the APK: copy it to %USERPROFILE%\\.s1_relay\\ on a new build PC\n"
        "and regenerate Assets/VRMaintenanceResearch/ResearchFlow/Resources/S1CloudRelay.json, or new APKs will not auto-upload.\n", encoding="utf-8")
    enroll = Path.home() / ".s1_relay" / "enroll_key.json"
    if enroll.exists():
        shutil.copy2(enroll, private / "enroll_key.json")
    print(json.dumps(dict(kit=str(out), private=str(private), identity=identity), indent=1, ensure_ascii=False))


if __name__ == "__main__":
    main()
