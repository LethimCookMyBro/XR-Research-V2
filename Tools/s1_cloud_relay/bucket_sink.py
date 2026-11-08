"""Railway Storage Bucket (S3-compatible) backend for the S1 relay.

Layout: Quest_01/cohort_DEV/participant_DEV01/session_<id>/run_<id>/bundle_<id>/{manifest.json,COMPLETE.json,raw/...}
A bundle becomes provider_verified only after every object is downloaded back from the bucket by key and its SHA-256
matches both the relay copy and the device manifest. Credentials come from Railway variable references, never the APK.
"""
import hashlib, json, os, re, threading, time
from pathlib import Path

NAMES = ("S1_BUCKET_ENDPOINT", "S1_BUCKET_NAME", "S1_BUCKET_ACCESS_KEY_ID", "S1_BUCKET_SECRET_ACCESS_KEY")
_LOCK = threading.Lock()


def ready():
    return all(os.environ.get(x) for x in NAMES)


def folder_label(label, device_id):
    text = re.sub(r"[^A-Za-z0-9_-]+", "_", (label or "").strip()).strip("_")
    return text or "device_" + device_id


def _sha(data):
    return hashlib.sha256(data).hexdigest()


class BucketSink:
    def __init__(self, client=None):
        if client is None:
            import boto3
            from botocore.config import Config
            client = boto3.client("s3", endpoint_url=os.environ["S1_BUCKET_ENDPOINT"],
                                  region_name=os.environ.get("S1_BUCKET_REGION") or "auto",
                                  aws_access_key_id=os.environ["S1_BUCKET_ACCESS_KEY_ID"],
                                  aws_secret_access_key=os.environ["S1_BUCKET_SECRET_ACCESS_KEY"],
                                  config=Config(s3={"addressing_style": os.environ.get("S1_BUCKET_URL_STYLE") or "virtual"},
                                                retries={"max_attempts": 3}))
        self.s3, self.bucket = client, os.environ.get("S1_BUCKET_NAME", "")

    def readback(self, key):
        h, size = hashlib.sha256(), 0
        body = self.s3.get_object(Bucket=self.bucket, Key=key)["Body"]
        for buf in iter(lambda: body.read(1024 * 1024), b""):
            h.update(buf); size += len(buf)
        return size, h.hexdigest()

    def put(self, key, path):
        data = Path(path).read_bytes()
        expected = (len(data), _sha(data))
        try:
            existing = self.readback(key)
        except self.s3.exceptions.NoSuchKey:
            existing = None
        if existing is not None and existing != expected:
            raise ValueError("Remote object conflict " + key.rsplit("/", 1)[-1])
        if existing is None:
            self.s3.put_object(Bucket=self.bucket, Key=key, Body=data, ContentType="application/octet-stream")
            if self.readback(key) != expected:
                raise ValueError("Bucket bytes differ from relay copy " + key.rsplit("/", 1)[-1])
        return {"key": key, "bytes": expected[0], "sha256": expected[1]}

    def sync(self, bundle):
        base = Path(bundle)
        m = json.loads((base / "manifest.json").read_text(encoding="utf-8"))
        i = m["identity"]
        device = json.loads((base / "relay_device.json").read_text(encoding="utf-8")) if (base / "relay_device.json").exists() else {}
        device_id = device.get("device_id") or "legacy"
        group = i["group"]
        cohort = "TH" if group == "Thai" else "JP" if group == "Japanese" else group.upper()
        prefix = "/".join([folder_label(device.get("device_label"), device_id), "cohort_" + cohort, "participant_" + i["participant_code"],
                           "session_" + i["session_id"], "run_" + i["run_id"], "bundle_" + m["bundle_id"]])
        uploaded = {}
        for name in sorted([*m["files"], "manifest.json", "COMPLETE.json"]):
            uploaded[name] = self.put(prefix + "/" + name, base / name)
            if name in m["files"] and uploaded[name]["sha256"] != m["files"][name]["sha256"]:
                raise ValueError("Bucket bytes differ from device manifest " + name)
        return dict(schema_version="s1.bucket-receipt.1", bundle_id=m["bundle_id"],
                    manifest_sha256=hashlib.sha256((base / "manifest.json").read_bytes()).hexdigest(),
                    device_id=device_id, device_label=device.get("device_label"),
                    status="provider_verified", provider_upload_confirmed=True, provider="railway_bucket",
                    destination=prefix, verified_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                    verification="sha256 of bytes downloaded back from the bucket by key", provider_files=uploaded)


def sync_forever(store, period=60):
    """Bucket first; Google Drive stays available as the alternative sink. Logs carry no identities or secrets."""
    from drive_sink import DriveSink, ready as drive_ready, sync_pending
    while True:
        try:
            with _LOCK:
                if ready():
                    sync_pending(store, BucketSink())
                elif drive_ready():
                    sync_pending(store, DriveSink())
        except Exception as ex:
            print("[S1Sink] Disabled by target/auth check: " + type(ex).__name__, flush=True)
        time.sleep(period)
