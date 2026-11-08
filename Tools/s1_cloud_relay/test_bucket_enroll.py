"""Zero-setup enrollment + Railway bucket sink. Loopback only, synthetic DEV bundles, fake S3 client."""
import io, json, os, sys, tempfile, threading, unittest
from http.server import ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from receiver import Store, Handler, sha
import bucket_sink, drive_sink
import test_multidevice as two

KEY = "InstallKeyForTestsOnly_0123456789abcdefghij"
INSTALL_A, INSTALL_B = "a" * 32, "b" * 32


class FakeS3:
    class exceptions:
        class NoSuchKey(Exception):
            pass

    def __init__(self, corrupt=None):
        self.objects, self.corrupt, self.puts = {}, corrupt, 0

    def put_object(self, Bucket, Key, Body, ContentType):
        self.puts += 1
        self.objects[Key] = Body + (b"!" if self.corrupt and Key.endswith(self.corrupt) else b"")

    def get_object(self, Bucket, Key):
        if Key not in self.objects:
            raise self.exceptions.NoSuchKey()
        return {"Body": io.BytesIO(self.objects[Key])}


class EnrollAndBucket(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        names = ("S1_RELAY_DEVICE_TOKENS_JSON", "S1_RELAY_TOKEN_SHA256", "S1_RELAY_ENROLL_KEY_SHA256", "S1_RELAY_MAX_DEVICES", "S1_RELAY_STUDY_APPROVAL_JSON")
        self.old = {k: os.environ.get(k) for k in names}
        for k in names:
            os.environ.pop(k, None)
        os.environ["S1_RELAY_ENROLL_KEY_SHA256"] = sha(KEY.encode())
        self.store = Handler.store = Store(self.tmp.name)
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.port = self.server.server_address[1]

    def tearDown(self):
        self.server.shutdown(); self.server.server_close()
        for k, v in self.old.items():
            if v is None: os.environ.pop(k, None)
            else: os.environ[k] = v

    def enroll(self, install, key=KEY):
        return two.call(self.port, "POST", "/v1/enroll", json.dumps({"install_id": install}).encode(), "", key)

    def test_installs_get_sequential_quest_folders_and_reinstall_keeps_folder(self):
        status, a = self.enroll(INSTALL_A); self.assertEqual((status, a["label"], a["allow_study"]), (200, "Quest_01", False))
        status, b = self.enroll(INSTALL_B); self.assertEqual((status, b["label"]), (200, "Quest_02"))
        status, again = self.enroll(INSTALL_A)
        self.assertEqual((status, again["label"]), (200, "Quest_01"))
        self.assertNotEqual(again["device_token"], a["device_token"])
        mb, cb, files = two.bundle()
        two.TOKENS[INSTALL_A] = a["device_token"]
        self.assertEqual(two.upload(self.port, INSTALL_A, mb, cb, files)[0], 401, "token replaced by reinstall is revoked")
        two.TOKENS[INSTALL_A] = again["device_token"]
        self.assertEqual(two.upload(self.port, INSTALL_A, mb, cb, files)[0], 200)

    def test_wrong_or_missing_key_and_device_cap(self):
        self.assertEqual(self.enroll(INSTALL_A, "wrong-key-0123456789abcdefghijklmnop")[0], 401)
        self.assertEqual(self.enroll("bad/id")[0], 400)
        os.environ["S1_RELAY_MAX_DEVICES"] = "1"
        self.assertEqual(self.enroll(INSTALL_A)[0], 200)
        self.assertEqual(self.enroll(INSTALL_B)[0], 403)
        self.assertEqual(self.enroll(INSTALL_A)[0], 200, "an enrolled Quest can always re-enroll")
        os.environ.pop("S1_RELAY_ENROLL_KEY_SHA256")
        self.assertEqual(self.enroll(INSTALL_A)[0], 401, "enrollment is off without the server-side key hash")

    def test_same_participant_on_two_quests_lands_in_separate_bucket_folders_with_readback(self):
        for install in (INSTALL_A, INSTALL_B):
            two.TOKENS[install] = self.enroll(install)[1]["device_token"]
        mb, cb, files = two.bundle()
        self.assertEqual(two.upload(self.port, INSTALL_A, mb, cb, files)[1]["state"], "staged_verified")
        self.assertEqual(two.upload(self.port, INSTALL_B, mb, cb, files)[1]["state"], "staged_verified")
        s3 = FakeS3(); sink = bucket_sink.BucketSink(s3)
        drive_sink.sync_pending(self.store, sink)
        bid = json.loads(mb)["bundle_id"]
        for install, label in ((INSTALL_A, "Quest_01"), (INSTALL_B, "Quest_02")):
            r = json.loads((Path(self.tmp.name) / ("device_" + install) / bid / "relay_receipt.json").read_text())
            self.assertEqual((r["status"], r["device_label"], r["provider"]), ("provider_verified", label, "railway_bucket"))
            self.assertEqual(r["destination"], label + "/cohort_DEV/participant_DEV001/session_s_same/run_r1/bundle_" + bid)
            self.assertEqual(r["provider_files"]["raw/events.jsonl"]["sha256"], sha(files["raw/events.jsonl"]))
            status, answer = two.call(self.port, "GET", "/v1/bundles/" + bid, b"", install, two.TOKENS[install])
            self.assertEqual((answer["state"], answer["provider_upload_confirmed"]), ("provider_verified", True))
        puts = s3.puts
        drive_sink.sync_pending(self.store, sink)
        self.assertEqual(s3.puts, puts, "verified bundles are not re-uploaded")

    def test_corrupted_bucket_bytes_stay_pending(self):
        two.TOKENS[INSTALL_A] = self.enroll(INSTALL_A)[1]["device_token"]
        mb, cb, files = two.bundle()
        two.upload(self.port, INSTALL_A, mb, cb, files)
        drive_sink.sync_pending(self.store, bucket_sink.BucketSink(FakeS3(corrupt="events.jsonl")))
        r = json.loads(next(Path(self.tmp.name).glob("device_*/*/relay_receipt.json")).read_text())
        self.assertEqual(r["status"], "staged_verified"); self.assertIn("differ", r["pending_reason"])

    def test_study_bundle_still_refused_without_approval(self):
        two.TOKENS[INSTALL_A] = self.enroll(INSTALL_A)[1]["device_token"]
        mb, cb, files = two.bundle(code="TH001", session="s_th", bundle_id="snapshot_th0123456789abcdef01234", test_data=False, group="Thai")
        self.assertEqual(two.upload(self.port, INSTALL_A, mb, cb, files)[0], 403)


if __name__ == "__main__":
    unittest.main()
