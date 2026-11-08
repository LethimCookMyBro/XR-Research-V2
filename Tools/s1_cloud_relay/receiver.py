"""S1 Quest HTTPS relay. DEV-only by default; no Google/API secret is ever placed in the APK.

Protocol: POST /v1/bundles (base64 raw manifest+COMPLETE),
GET /v1/bundles/<id>, PUT /v1/bundles/<id>/chunk (X-File, X-Offset, X-Chunk-SHA256),
POST /v1/bundles/<id>/finalize. All /v1 endpoints require a device Bearer token.
Persistent DATA_ROOT volume required when deployed. No study traffic without separate approval.
"""
import base64, hashlib, hmac, json, os, re, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

FILES = {"raw/events.jsonl", "raw/movement.csv", "raw/session_metadata.json", "raw/research-session.json"}
TOKEN = re.compile(r"^[A-Za-z0-9_-]{1,160}$")
HEX = re.compile(r"^[a-f0-9]{64}$")
MAX_CHUNK = 512 * 1024
LOCK = threading.RLock()


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def parse(data):
    return json.loads(data.decode("utf-8"))


def safe(value):
    if not isinstance(value, str) or not TOKEN.fullmatch(value):
        raise ValueError("Invalid identifier")
    return value


def device_registry():
    """Enrolled Quests: {"<device_id>": "<sha256>"} or {"<device_id>": {"token_sha256": "...", "label": "Quest 1"}}."""
    registry = os.environ.get("S1_RELAY_DEVICE_TOKENS_JSON", "")
    if registry:
        try:
            tokens = json.loads(registry)
        except (ValueError, TypeError):
            return {}
        if not isinstance(tokens, dict):
            return {}
        devices = {}
        for key, value in tokens.items():
            entry = value if isinstance(value, dict) else {"token_sha256": value}
            digest = entry.get("token_sha256")
            if TOKEN.fullmatch(key) and isinstance(digest, str) and HEX.fullmatch(digest):
                devices[key] = {"token_sha256": digest, "label": str(entry.get("label") or key)[:60]}
        return devices
    legacy = os.environ.get("S1_RELAY_TOKEN_SHA256", "")
    return {"": {"token_sha256": legacy, "label": "legacy DEV device"}} if HEX.fullmatch(legacy) else {}


def configured_tokens():
    return {key: value["token_sha256"] for key, value in device_registry().items()}


def enroll_key_sha():
    value = os.environ.get("S1_RELAY_ENROLL_KEY_SHA256", "")
    return value if HEX.fullmatch(value) else ""


class Enrollment:
    """Self-enrolled Quests on the relay volume: {install_id: {token_sha256, label, enrolled_utc}}.
    The shared install key in the APK can only enroll (capped by S1_RELAY_MAX_DEVICES); each Quest then uploads with its own token
    and only into its own Quest_NN namespace. Nothing here can read, list or delete stored data."""

    def __init__(self, root):
        self.path = Path(root) / "_enrolled_devices.json"

    def load(self):
        return json.loads(self.path.read_text(encoding="utf-8")) if self.path.exists() else {}

    def enroll(self, install_id):
        safe(install_id)
        with LOCK:
            devices = self.load()
            entry = devices.get(install_id)
            if entry is None:
                if len(devices) >= int(os.environ.get("S1_RELAY_MAX_DEVICES", "20")):
                    raise PermissionError("Device limit reached")
                taken = {d["label"] for d in devices.values()}
                # Simulated test clients never consume real headset numbers.
                pattern = "SIM_%02d" if install_id.startswith("devsim") else "Quest_%02d"
                n = 1
                while pattern % n in taken:
                    n += 1
                entry = {"label": pattern % n, "enrolled_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
            # Reinstalling the app loses its token: the same install ID keeps its folder and gets a fresh token.
            token = base64.urlsafe_b64encode(os.urandom(32)).rstrip(b"=").decode()
            devices[install_id] = dict(entry, token_sha256=sha(token.encode()))
            temp = self.path.with_suffix(".next")
            temp.write_text(json.dumps(devices, indent=1, sort_keys=True), encoding="utf-8")
            os.replace(temp, self.path)
            return {"device_id": install_id, "device_token": token, "label": entry["label"], "allow_study": study_approval() is not None}

    def get(self, install_id):
        entry = self.load().get(install_id)
        return {"token_sha256": entry["token_sha256"], "label": entry["label"]} if entry else None


STUDY_APPROVAL_FIELDS = ("approved_by", "ethics_ref", "approved_utc", "region", "access_scope")


def study_approval():
    """Study (TH/JP) uploads stay refused unless the real approval is configured server-side; the relay does not validate it."""
    try:
        approval = json.loads(os.environ.get("S1_RELAY_STUDY_APPROVAL_JSON", "") or "null")
    except ValueError:
        return None
    if not isinstance(approval, dict) or approval.get("encryption_at_rest") is not True:
        return None
    return approval if all(isinstance(approval.get(k), str) and approval[k].strip() for k in STUDY_APPROVAL_FIELDS) else None


class Store:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def folder(self, bundle, device=""):
        # Each enrolled Quest has its own namespace, so equal participant codes or bundle IDs on two headsets never meet.
        return (self.root / ("device_" + safe(device)) if device else self.root) / safe(bundle)

    def request(self, payload, device="", label=None):
        m = base64.b64decode(payload["manifest_b64"], validate=True)
        c = base64.b64decode(payload["complete_b64"], validate=True)
        if len(m) > 100_000 or len(c) > 10_000:
            raise ValueError("Manifest too large")
        manifest, complete = parse(m), parse(c)
        bundle = safe(manifest["bundle_id"])
        if manifest.get("schema_version") != "researchflow.device-bundle.1":
            raise ValueError("Wrong device manifest schema")
        if complete.get("schema_version") != "researchflow.bundle-complete.1" or complete.get("bundle_id") != bundle or complete.get("manifest_sha256") != sha(m):
            raise ValueError("Unverified device completion marker")
        ident = manifest["identity"]
        for k in ("participant_code", "session_id", "run_id"):
            safe(ident[k])
        group = ident.get("group")
        technical = ident.get("test_data") is True and re.fullmatch(r"(DEV|TEST|PILOT)[0-9]+", ident["participant_code"]) and group in ("Dev", "Test", "Pilot")
        study = ident.get("test_data") is False and group in ("Thai", "Japanese") and study_approval() is not None
        if not technical and not study:
            raise PermissionError("Study data is disabled pending ethics/provider authorization")
        files = manifest.get("files")
        if not isinstance(files, dict) or set(files) != FILES:
            raise ValueError("Unexpected raw file allowlist")
        for name, info in files.items():
            if not isinstance(info.get("bytes"), int) or not 0 <= info["bytes"] <= 2_000_000_000 or not HEX.fullmatch(info.get("sha256", "")):
                raise ValueError("Invalid size or digest " + name)
        folder = self.folder(bundle, device)
        with LOCK:
            folder.mkdir(parents=True, exist_ok=True)
            enrolled = {"schema_version": "s1.relay-device.1", "device_id": device or "legacy", "device_label": label or device or "legacy"}
            if not (folder / "relay_device.json").exists():
                (folder / "relay_device.json").write_bytes(encoded(dict(enrolled, first_seen_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))))
            for name, data in (("manifest.json", m), ("COMPLETE.json", c)):
                target = folder / name
                if target.exists() and target.read_bytes() != data:
                    raise ValueError("Existing bundle identity differs")
                if not target.exists():
                    target.write_bytes(data)
            (folder / "raw").mkdir(exist_ok=True)
            return self.status(bundle, device)

    def status(self, bundle, device=""):
        folder = self.folder(bundle, device)
        manifest = parse((folder / "manifest.json").read_bytes())
        files = {}
        for name, meta in manifest["files"].items():
            path = folder / name
            files[name] = min(path.stat().st_size, meta["bytes"]) if path.exists() else 0
        state = "receiving"
        receipt = folder / "relay_receipt.json"
        confirmed = False
        if receipt.exists():
            record = parse(receipt.read_bytes())
            state = record["status"]
            confirmed = record.get("provider_upload_confirmed") is True
        pending = parse(receipt.read_bytes()).get("pending_reason") if receipt.exists() else None
        return {"bundle_id": safe(bundle), "state": state, "offsets": files,
                "provider_upload_confirmed": confirmed, "pending_reason": pending, "device_id": device or "legacy"}

    def chunk(self, bundle, name, offset, chunk, digest, device=""):
        folder = self.folder(bundle, device)
        manifest = parse((folder / "manifest.json").read_bytes())
        if name not in FILES or not HEX.fullmatch(digest or "") or sha(chunk) != digest or not chunk or len(chunk) > MAX_CHUNK:
            raise ValueError("Invalid chunk")
        meta = manifest["files"][name]
        path = folder / name
        with LOCK:
            if (folder / "relay_receipt.json").exists():
                raise ValueError("Bundle already finalized")
            current = path.stat().st_size if path.exists() else 0
            if offset > current or offset < 0:
                return {"next_offset": current, "retry": True}
            if offset < current:
                with path.open("rb") as f:
                    f.seek(offset)
                    if f.read(len(chunk)) != chunk:
                        raise ValueError("Conflicting retry bytes")
                return {"next_offset": current, "retry": True}
            if current + len(chunk) > meta["bytes"]:
                raise ValueError("Chunk exceeds declared file size")
            with path.open("ab") as f:
                f.write(chunk)
                f.flush()
                os.fsync(f.fileno())
            return {"next_offset": current + len(chunk), "retry": False}

    def finish(self, bundle, device=""):
        folder = self.folder(bundle, device)
        with LOCK:
            data = (folder / "manifest.json").read_bytes()
            marker = parse((folder / "COMPLETE.json").read_bytes())
            if sha(data) != marker["manifest_sha256"]:
                raise ValueError("Manifest changed")
            manifest = parse(data)
            for name, meta in manifest["files"].items():
                path = folder / name
                if not path.is_file() or path.stat().st_size != meta["bytes"]:
                    raise ValueError("Incomplete file: " + name)
                h = hashlib.sha256()
                with path.open("rb") as f:
                    for buf in iter(lambda: f.read(1024 * 1024), b""):
                        h.update(buf)
                if h.hexdigest() != meta["sha256"]:
                    raise ValueError("Hash mismatch: " + name)
            receipt = folder / "relay_receipt.json"
            if not receipt.exists():
                enrolled = parse((folder / "relay_device.json").read_bytes()) if (folder / "relay_device.json").exists() else {}
                receipt.write_bytes(encoded({"schema_version": "s1.relay-receipt.1",
                    "bundle_id": bundle, "manifest_sha256": sha(data),
                    "device_id": enrolled.get("device_id", device or "legacy"), "device_label": enrolled.get("device_label"),
                    "staged_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                    "status": "staged_verified", "provider_upload_confirmed": False}))
            return self.status(bundle, device)


class Handler(BaseHTTPRequestHandler):
    store = None
    device, label = "", None
    def log_message(self, fmt, *args):
        # Do not emit bearer tokens, request bodies, participant IDs or file paths.
        print("relay", self.command, self.path.split("?")[0].split("/")[1:3], flush=True)

    def send(self, status, value):
        body = encoded(value)
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def allowed(self):
        supplied = self.headers.get("Authorization", "")
        if not supplied.startswith("Bearer "):
            return False
        header = self.headers.get("X-Device-Id")
        try:
            device_id = safe(header) if header or os.environ.get("S1_RELAY_DEVICE_TOKENS_JSON") else ""
        except ValueError:
            return False
        entry = device_registry().get(device_id) or (Enrollment(self.store.root).get(device_id) if device_id else None)
        if entry is None and not os.environ.get("S1_RELAY_DEVICE_TOKENS_JSON"):
            device_id, entry = "", device_registry().get("")  # legacy single-token DEV mode ignores the device header
        if not entry or not hmac.compare_digest(entry["token_sha256"], sha(supplied[7:].encode())):
            return False
        self.device, self.label = device_id, entry["label"]
        return True

    def enroll(self):
        supplied = self.headers.get("Authorization", "")
        key = enroll_key_sha()
        length = int(self.headers.get("Content-Length", 0) or 0)
        if not key or not supplied.startswith("Bearer ") or not hmac.compare_digest(key, sha(supplied[7:].encode())) or not 0 < length <= 2000:
            self.send(401, {"error": "unauthorized"})
            return
        try:
            self.send(200, Enrollment(self.store.root).enroll(parse(self.rfile.read(length))["install_id"]))
        except PermissionError:
            self.send(403, {"error": "device_limit"})
        except (ValueError, KeyError, TypeError, json.JSONDecodeError):
            self.send(400, {"error": "invalid_install_id"})

    def api(self):
        if self.command == "POST" and urlsplit(self.path).path.rstrip("/") == "/v1/enroll":
            self.enroll()
            return
        if not self.allowed():
            self.send(401, {"error": "unauthorized"})
            # Drain accepted-size bodies before close so clients receive the denial instead of a reset.
            timeout = self.connection.gettimeout()
            try:
                length = int(self.headers.get("Content-Length", 0))
                if 0 < length <= (MAX_CHUNK if self.command == "PUT" else 150_000):
                    deadline = time.monotonic() + 1
                    while length > 0:
                        remaining = deadline - time.monotonic()
                        if remaining <= 0:
                            break
                        self.connection.settimeout(remaining)
                        data = self.rfile.read1(min(length, 65536))
                        if not data:
                            break
                        length -= len(data)
            except (ValueError, OSError):
                pass
            finally:
                self.connection.settimeout(timeout)
            return
        pieces = [p for p in urlsplit(self.path).path.split("/") if p]
        if pieces[:2] != ["v1", "bundles"]:
            self.send(404, {"error": "not_found"})
            return
        try:
            length = int(self.headers.get("Content-Length", 0))
            limit = MAX_CHUNK if self.command == "PUT" else 150_000
            if length < 0 or length > limit:
                self.send(413, {"error": "size_limit"})
                return
            data = self.rfile.read(length)
            if self.command == "POST" and len(pieces) == 2:
                result = self.store.request(parse(data), self.device, self.label)
            elif self.command == "GET" and len(pieces) == 3:
                result = self.store.status(safe(pieces[2]), self.device)
            elif self.command == "PUT" and len(pieces) == 4 and pieces[3] == "chunk":
                result = self.store.chunk(safe(pieces[2]), self.headers.get("X-File"),
                    int(self.headers.get("X-Offset", -1)), data, self.headers.get("X-Chunk-SHA256"), self.device)
            elif self.command == "POST" and len(pieces) == 4 and pieces[3] == "finalize":
                result = self.store.finish(safe(pieces[2]), self.device)
            else:
                self.send(404, {"error": "not_found"})
                return
            self.send(200, result)
        except PermissionError:
            self.send(403, {"error": "study_upload_disabled"})
        except (ValueError, KeyError, TypeError, OSError, json.JSONDecodeError) as e:
            self.send(400, {"error": str(e)[:125]})

    def do_GET(self):
        if self.path == "/health":
            from bucket_sink import ready as bucket_ready
            self.send(200, {"status": "ok", "upload_enabled": bool(configured_tokens() or enroll_key_sha()),
                            "auto_enroll": bool(enroll_key_sha()), "bucket_ready": bucket_ready()})
        else:
            self.api()

    def do_POST(self):
        self.api()

    def do_PUT(self):
        self.api()


def main():
    root = os.environ.get("S1_RELAY_DATA_ROOT", "")
    if not root or not Path(root).is_absolute():
        raise SystemExit("S1_RELAY_DATA_ROOT must be an explicit persistent absolute path")
    Handler.store = Store(root)
    from bucket_sink import sync_forever
    threading.Thread(target=sync_forever, args=(Handler.store,), daemon=True).start()
    port = int(os.environ.get("PORT", "8080"))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
