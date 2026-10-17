"""S1 Quest HTTPS relay. DEV-only by default; no Google/API secret is ever placed in the APK.

Protocol: POST /v1/bundles (base64 raw manifest+COMPLETE),
GET /v1/bundles/<id>, PUT /v1/bundles/<id>/chunk (X-File, X-Offset, X-Chunk-SHA256),
POST /v1/bundles/<id>/finalize. All /v1 endpoints require a device Bearer token.
Persistent DATA_ROOT volume required when deployed. No study traffic without separate approval.
"""
import base64, hashlib, hmac, json, os, re, threading
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


class Store:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def folder(self, bundle):
        return self.root / safe(bundle)

    def request(self, payload):
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
        if not ident.get("test_data") or not re.fullmatch(r"(DEV|TEST|PILOT)[0-9]+", ident["participant_code"]) or group not in ("Dev", "Test", "Pilot"):
            # Deliberately no participant-study uploads in this release.
            raise PermissionError("Study data is disabled pending ethics/provider authorization")
        files = manifest.get("files")
        if not isinstance(files, dict) or set(files) != FILES:
            raise ValueError("Unexpected raw file allowlist")
        for name, info in files.items():
            if not isinstance(info.get("bytes"), int) or not 0 <= info["bytes"] <= 2_000_000_000 or not HEX.fullmatch(info.get("sha256", "")):
                raise ValueError("Invalid size or digest " + name)
        folder = self.folder(bundle)
        with LOCK:
            folder.mkdir(exist_ok=True)
            for name, data in (("manifest.json", m), ("COMPLETE.json", c)):
                target = folder / name
                if target.exists() and target.read_bytes() != data:
                    raise ValueError("Existing bundle identity differs")
                if not target.exists():
                    target.write_bytes(data)
            (folder / "raw").mkdir(exist_ok=True)
            return self.status(bundle)

    def status(self, bundle):
        folder = self.folder(bundle)
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
        return {"bundle_id": safe(bundle), "state": state, "offsets": files,
                "provider_upload_confirmed": confirmed}

    def chunk(self, bundle, name, offset, chunk, digest):
        folder = self.folder(bundle)
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

    def finish(self, bundle):
        folder = self.folder(bundle)
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
                receipt.write_bytes(encoded({"schema_version": "s1.relay-receipt.1",
                    "bundle_id": bundle, "manifest_sha256": sha(data),
                    "status": "staged_verified", "provider_upload_confirmed": False}))
            return self.status(bundle)


class Handler(BaseHTTPRequestHandler):
    store = None
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
        expected = os.environ.get("S1_RELAY_TOKEN_SHA256", "")  # single-device DEV fallback
        registry = os.environ.get("S1_RELAY_DEVICE_TOKENS_JSON", "")
        if registry:
            try:
                device_id = safe(self.headers.get("X-Device-Id"))
                expected = json.loads(registry).get(device_id, "")
            except (ValueError, TypeError, json.JSONDecodeError):
                return False
        return bool(HEX.fullmatch(expected)) and hmac.compare_digest(expected, sha(supplied[7:].encode()))

    def api(self):
        if not self.allowed():
            self.send(401, {"error": "unauthorized"})
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
                result = self.store.request(parse(data))
            elif self.command == "GET" and len(pieces) == 3:
                result = self.store.status(safe(pieces[2]))
            elif self.command == "PUT" and len(pieces) == 4 and pieces[3] == "chunk":
                result = self.store.chunk(safe(pieces[2]), self.headers.get("X-File"),
                    int(self.headers.get("X-Offset", -1)), data, self.headers.get("X-Chunk-SHA256"))
            elif self.command == "POST" and len(pieces) == 4 and pieces[3] == "finalize":
                result = self.store.finish(safe(pieces[2]))
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
            self.send(200, {"status": "ok", "upload_enabled": bool(os.environ.get("S1_RELAY_TOKEN_SHA256"))})
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
    from drive_sink import sync_forever
    threading.Thread(target=sync_forever, args=(Handler.store,), daemon=True).start()
    port = int(os.environ.get("PORT", "8080"))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
