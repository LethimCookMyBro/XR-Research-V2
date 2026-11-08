"""Two-Quest simulation for the relay and the Drive sink. Loopback only, synthetic DEV bundles, fake Drive API.
The client below follows the same protocol as Assets/.../ResearchQuestCloudUploader.cs; it does not run the C# uploader."""
import base64, hashlib, http.client, json, os, sys, tempfile, threading, types, unittest
from http.server import ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from receiver import Store, Handler, sha, FILES
import drive_sink

CHUNK = 256 * 1024
TOKENS = {"QUEST_A": "TokenForQuestA_0123456789abcdefghijklmnop", "QUEST_B": "TokenForQuestB_0123456789abcdefghijklmnop"}


def bundle(code="DEV001", session="s_same", bundle_id="snapshot_same0123456789abcdef01", seed=b"A", test_data=True, group="Dev"):
    files = {name: (seed + name.encode()) * (40000 if name.endswith("movement.csv") else 300) for name in sorted(FILES)}
    meta = {name: {"bytes": len(b), "sha256": sha(b)} for name, b in files.items()}
    m = {"schema_version": "researchflow.device-bundle.1", "bundle_id": bundle_id, "files": meta,
         "identity": {"participant_code": code, "session_id": session, "run_id": "r1", "group": group, "test_data": test_data}}
    mb = json.dumps(m, sort_keys=True).encode()
    cb = json.dumps({"schema_version": "researchflow.bundle-complete.1", "bundle_id": bundle_id, "manifest_sha256": sha(mb)}, sort_keys=True).encode()
    return mb, cb, files


def call(port, method, path, body, device, token, headers=None):
    c = http.client.HTTPConnection("127.0.0.1", port, timeout=20)
    h = {"Authorization": "Bearer " + token, "X-Device-Id": device, "Content-Length": str(len(body))}
    h.update(headers or {})
    c.request(method, path, body=body, headers=h)
    r = c.getresponse()
    data = json.loads(r.read() or b"{}")
    c.close()
    return r.status, data


def upload(port, device, mb, cb, files, stop_after=None):
    """Mirror of the Quest uploader: init -> resume from relay offsets -> 256 KiB chunks -> finalize. Returns final status."""
    token = TOKENS[device]
    init = json.dumps({"manifest_b64": base64.b64encode(mb).decode(), "complete_b64": base64.b64encode(cb).decode()}).encode()
    status, answer = call(port, "POST", "/v1/bundles", init, device, token)
    if status != 200:
        return status, answer
    bid = json.loads(mb)["bundle_id"]
    sent = 0
    for name in sorted(files):
        offset = answer["offsets"][name]
        while offset < len(files[name]):
            if stop_after is not None and sent >= stop_after:
                return 0, {"state": "network_dropped"}
            block = files[name][offset:offset + CHUNK]
            status, reply = call(port, "PUT", f"/v1/bundles/{bid}/chunk", block, device, token,
                                 {"X-File": name, "X-Offset": str(offset), "X-Chunk-SHA256": sha(block)})
            if status != 200:
                return status, reply
            offset = reply["next_offset"]; sent += 1
    return call(port, "POST", f"/v1/bundles/{bid}/finalize", b"", device, token)


class TwoQuestRelay(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.old = {k: os.environ.get(k) for k in ("S1_RELAY_DEVICE_TOKENS_JSON", "S1_RELAY_TOKEN_SHA256", "S1_RELAY_STUDY_APPROVAL_JSON")}
        os.environ["S1_RELAY_DEVICE_TOKENS_JSON"] = json.dumps({
            "QUEST_A": {"token_sha256": sha(TOKENS["QUEST_A"].encode()), "label": "Quest 1"},
            "QUEST_B": sha(TOKENS["QUEST_B"].encode())})
        os.environ.pop("S1_RELAY_STUDY_APPROVAL_JSON", None)
        self.start()

    def start(self):
        Handler.store = Store(self.tmp.name)
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.port = self.server.server_address[1]

    def tearDown(self):
        self.server.shutdown(); self.server.server_close()
        for k, v in self.old.items():
            if v is None: os.environ.pop(k, None)
            else: os.environ[k] = v

    def test_same_participant_and_bundle_on_two_quests_upload_concurrently_without_collision(self):
        a, b = bundle(seed=b"A"), bundle(seed=b"B")  # identical DEV001/session/bundle IDs, different bytes
        results = {}
        threads = [threading.Thread(target=lambda d, x: results.__setitem__(d, upload(self.port, d, *x)), args=(d, x))
                   for d, x in (("QUEST_A", a), ("QUEST_B", b))]
        [t.start() for t in threads]; [t.join() for t in threads]
        for device, data in (("QUEST_A", a), ("QUEST_B", b)):
            status, answer = results[device]
            self.assertEqual((status, answer["state"], answer["device_id"]), (200, "staged_verified", device))
            folder = Path(self.tmp.name) / ("device_" + device) / "snapshot_same0123456789abcdef01"
            for name, content in data[2].items():
                self.assertEqual((folder / name).read_bytes(), content, device + " " + name)
            receipt = json.loads((folder / "relay_receipt.json").read_text())
            self.assertEqual(receipt["device_id"], device)
        self.assertEqual(json.loads((Path(self.tmp.name) / "device_QUEST_A/snapshot_same0123456789abcdef01/relay_receipt.json").read_text())["device_label"], "Quest 1")

    def test_one_quest_drops_offline_then_resumes_after_relay_restart_without_duplicate_bytes(self):
        a, b = bundle(seed=b"A"), bundle(code="DEV002", session="s_b", bundle_id="snapshot_b0123456789abcdef0123", seed=b"B")
        self.assertEqual(upload(self.port, "QUEST_A", *a, stop_after=1)[1]["state"], "network_dropped")
        self.assertEqual(upload(self.port, "QUEST_B", *b)[1]["state"], "staged_verified")  # the other Quest is unaffected
        self.server.shutdown(); self.server.server_close(); self.start()  # relay restart while Quest A is offline
        status, answer = upload(self.port, "QUEST_A", *a)
        self.assertEqual((status, answer["state"]), (200, "staged_verified"))
        folder = Path(self.tmp.name) / "device_QUEST_A" / "snapshot_same0123456789abcdef01"
        for name, content in a[2].items():
            self.assertEqual((folder / name).read_bytes(), content)
        again = upload(self.port, "QUEST_A", *a)  # repeat send after success: no new bytes, same state
        self.assertEqual((again[0], again[1]["state"]), (200, "staged_verified"))

    def test_token_bound_to_its_device_and_study_gate(self):
        mb, cb, files = bundle()
        init = json.dumps({"manifest_b64": base64.b64encode(mb).decode(), "complete_b64": base64.b64encode(cb).decode()}).encode()
        self.assertEqual(call(self.port, "POST", "/v1/bundles", init, "QUEST_B", TOKENS["QUEST_A"])[0], 401)
        self.assertEqual(call(self.port, "POST", "/v1/bundles", init, "QUEST_C", TOKENS["QUEST_A"])[0], 401)
        smb, scb, _ = bundle(code="TH001", session="s_th", bundle_id="snapshot_th0123456789abcdef01234", test_data=False, group="Thai")
        sinit = json.dumps({"manifest_b64": base64.b64encode(smb).decode(), "complete_b64": base64.b64encode(scb).decode()}).encode()
        self.assertEqual(call(self.port, "POST", "/v1/bundles", sinit, "QUEST_A", TOKENS["QUEST_A"])[0], 403)
        os.environ["S1_RELAY_STUDY_APPROVAL_JSON"] = json.dumps({"approved_by": "x", "ethics_ref": "x", "approved_utc": "x", "region": "x", "access_scope": "x"})
        self.assertEqual(call(self.port, "POST", "/v1/bundles", sinit, "QUEST_A", TOKENS["QUEST_A"])[0], 403, "encryption_at_rest is required")
        os.environ["S1_RELAY_STUDY_APPROVAL_JSON"] = json.dumps({"approved_by": "x", "ethics_ref": "x", "approved_utc": "x", "region": "x", "access_scope": "x", "encryption_at_rest": True})
        self.assertEqual(call(self.port, "POST", "/v1/bundles", sinit, "QUEST_A", TOKENS["QUEST_A"])[0], 200)


class FakeDrive:
    """In-memory Drive v3 subset used by DriveSink: files get/list/create/get_media, permissions list."""
    def __init__(self, permissions, corrupt=None):
        self.items = {"ROOT": {"id": "ROOT", "name": "research_s1", "mimeType": "application/vnd.google-apps.folder", "parents": []}}
        self.blobs, self.perms, self.corrupt, self.n = {}, permissions, corrupt, 0
    def files(self): return self
    def permissions(self): return types.SimpleNamespace(list=lambda **k: types.SimpleNamespace(execute=lambda: {"permissions": self.perms}))
    def get(self, fileId, **k):
        return types.SimpleNamespace(execute=lambda: dict(self.items[fileId], capabilities={"canAddChildren": True}))
    def list(self, q, **k):
        parent = q.split("'")[1]; name = q.split("name = '")[1].split("'")[0]
        found = [dict(f, size=str(len(self.blobs.get(f["id"], b"")))) for f in self.items.values() if parent in f["parents"] and f["name"] == name]
        return types.SimpleNamespace(execute=lambda: {"files": found})
    def create(self, body, media_body=None, **k):
        self.n += 1; fid = "F%d" % self.n
        self.items[fid] = {"id": fid, "name": body["name"], "mimeType": body.get("mimeType", "application/octet-stream"), "parents": body["parents"]}
        if media_body is not None:
            data = Path(media_body.path).read_bytes()
            self.blobs[fid] = data + b"X" if self.corrupt == body["name"] else data
        return types.SimpleNamespace(execute=lambda: {"id": fid}, next_chunk=lambda num_retries=0: (None, {"id": fid, "size": str(len(self.blobs.get(fid, b"")))}))
    def get_media(self, fileId, **k):
        return self.blobs[fileId]


class FakeHttp(types.ModuleType):
    class MediaFileUpload:
        def __init__(self, path, **k): self.path = path
    class MediaIoBaseDownload:
        def __init__(self, fh, data, **k): self.fh, self.data = fh, data
        def next_chunk(self, num_retries=0): self.fh.write(self.data); return None, True


class DriveSinkLayoutAndVerification(unittest.TestCase):
    def setUp(self):
        sys.modules["googleapiclient"] = types.ModuleType("googleapiclient"); sys.modules["googleapiclient.http"] = FakeHttp("googleapiclient.http")
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.old = {k: os.environ.get(k) for k in ("S1_RELAY_DEVICE_TOKENS_JSON", "S1_GOOGLE_BROAD_FOLDER_ACK")}
        os.environ["S1_RELAY_DEVICE_TOKENS_JSON"] = json.dumps({"QUEST_A": sha(TOKENS["QUEST_A"].encode())})
        os.environ.pop("S1_GOOGLE_BROAD_FOLDER_ACK", None)
        self.store = Store(self.tmp.name)
        mb, cb, files = bundle()
        self.store.request({"manifest_b64": base64.b64encode(mb).decode(), "complete_b64": base64.b64encode(cb).decode()}, "QUEST_A", "Quest 1")
        bid = json.loads(mb)["bundle_id"]
        for name, data in files.items():
            for off in range(0, len(data), CHUNK):
                self.store.chunk(bid, name, off, data[off:off + CHUNK], sha(data[off:off + CHUNK]), "QUEST_A")
        self.store.finish(bid, "QUEST_A")
        self.receipt = Path(self.tmp.name) / "device_QUEST_A" / bid / "relay_receipt.json"

    def tearDown(self):
        for k, v in self.old.items():
            if v is None: os.environ.pop(k, None)
            else: os.environ[k] = v

    def sink(self, perms, corrupt=None):
        s = object.__new__(drive_sink.DriveSink); s.api = FakeDrive(perms, corrupt); s.parent = "ROOT"; s.access = s.confirm_restricted(); return s

    def test_restricted_folder_gets_device_tree_and_sha256_readback(self):
        s = self.sink([{"type": "user", "role": "owner"}])
        drive_sink.sync_pending(self.store, s)
        r = json.loads(self.receipt.read_text())
        self.assertEqual((r["status"], r["device_id"], r["device_label"], r["folder_access"]), ("provider_verified", "QUEST_A", "Quest 1", "restricted"))
        self.assertEqual(r["destination"], "site_TMU/device_QUEST_A/cohort_DEV/participant_DEV001/session_s_same/run_r1/bundle_snapshot_same0123456789abcdef01")
        self.assertFalse(r["destination"].startswith("research_s1"))
        manifest = json.loads((self.receipt.parent / "manifest.json").read_text())
        for name, meta in manifest["files"].items():
            self.assertEqual(r["provider_files"][name]["sha256"], meta["sha256"])
        drive_sink.sync_pending(self.store, s)  # second pass: nothing re-uploaded
        self.assertEqual(json.loads(self.receipt.read_text())["provider_files"], r["provider_files"])

    def test_corrupted_provider_bytes_stay_pending_with_reason(self):
        s = self.sink([{"type": "user", "role": "owner"}], corrupt="events.jsonl")
        drive_sink.sync_pending(self.store, s)
        r = json.loads(self.receipt.read_text())
        self.assertEqual(r["status"], "staged_verified"); self.assertIn("differ", r["pending_reason"])

    def test_owner_anyone_writer_folder_needs_ack_and_never_takes_study_data(self):
        broad = [{"type": "anyone", "role": "writer"}, {"type": "user", "role": "owner"}]
        with self.assertRaises(PermissionError):
            self.sink(broad)
        os.environ["S1_GOOGLE_BROAD_FOLDER_ACK"] = "OWNER_DEV_ONLY_TEST"
        s = self.sink(broad)
        drive_sink.sync_pending(self.store, s)
        r = json.loads(self.receipt.read_text())
        self.assertEqual((r["status"], r["folder_access"]), ("provider_verified", "broad_acknowledged:anyone/writer"))
        manifest = json.loads((self.receipt.parent / "manifest.json").read_text()); manifest["identity"]["test_data"] = False
        study = Path(self.tmp.name) / "study_copy"; study.mkdir(); (study / "manifest.json").write_text(json.dumps(manifest))
        with self.assertRaises(PermissionError):
            s.sync(study)


if __name__ == "__main__":
    unittest.main()
