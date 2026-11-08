"""Download everything in the S1 Railway bucket to a PC folder and verify each bundle against its device manifest.

Run through the Railway CLI so the bucket keys are injected from the relay service and never written to disk:
  railway run -s s1-quest-relay -e production python Tools/s1_cloud_relay/pull_bucket.py --dest D:/S1_Cloud_Pull
(Tools/delivery/Pull-Cloud.ps1 does exactly this.) Existing identical files are skipped; nothing in the bucket is changed.
"""
import argparse, hashlib, json, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from bucket_sink import BucketSink, ready


def sha_file(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for buf in iter(lambda: f.read(1024 * 1024), b""): h.update(buf)
    return h.hexdigest()


def pull(sink, dest):
    dest = Path(dest).resolve(); dest.mkdir(parents=True, exist_ok=True)
    got = skipped = 0
    for page in sink.s3.get_paginator("list_objects_v2").paginate(Bucket=sink.bucket):
        for obj in page.get("Contents", []):
            target = (dest / obj["Key"]).resolve()
            if dest not in target.parents: raise ValueError("Unsafe key " + obj["Key"])
            if target.exists() and target.stat().st_size == obj["Size"]:
                skipped += 1; continue
            target.parent.mkdir(parents=True, exist_ok=True)
            sink.s3.download_file(sink.bucket, obj["Key"], str(target)); got += 1
    bundles = []
    for manifest in sorted(dest.glob("*/cohort_*/participant_*/session_*/run_*/bundle_*/manifest.json")):
        m = json.loads(manifest.read_text(encoding="utf-8"))
        bad = [n for n, meta in m["files"].items() if not (manifest.parent / n).is_file() or sha_file(manifest.parent / n) != meta["sha256"]]
        bundles.append({"quest": manifest.relative_to(dest).parts[0], "bundle": str(manifest.parent.relative_to(dest)).replace("\\", "/"),
                        "verified": not bad, "mismatched": bad})
    report = {"downloaded": got, "skipped_existing": skipped, "bundles": len(bundles),
              "verified": sum(b["verified"] for b in bundles), "items": bundles}
    (dest / "PULL_REPORT.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    return report


def main():
    p = argparse.ArgumentParser(); p.add_argument("--dest", required=True); a = p.parse_args()
    if not ready(): raise SystemExit("Bucket variables missing: run through `railway run -s s1-quest-relay -e production ...`")
    r = pull(BucketSink(), a.dest)
    for b in r["items"]: print(("OK  " if b["verified"] else "BAD ") + b["bundle"])
    print("downloaded %(downloaded)d, skipped %(skipped_existing)d, bundles verified %(verified)d/%(bundles)d" % r)
    return 0 if r["verified"] == r["bundles"] else 2


if __name__ == "__main__":
    sys.exit(main())
