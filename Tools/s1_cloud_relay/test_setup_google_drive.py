"""Offline tests for setup_google_drive.py: no network, fake Drive REST opener."""
import base64, hashlib, io, json, re, sys, unittest, urllib.parse
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import setup_google_drive as setup


class FakeResponse(io.BytesIO):
    def __enter__(self): return self
    def __exit__(self, *a): return False


class FakeDriveRest:
    def __init__(self, broad=False):
        self.files, self.blobs, self.broad, self.n = {}, {}, broad, 0

    def __call__(self, request, timeout=None):
        url, method, body = request.full_url, request.get_method(), request.data
        assert request.headers.get("Authorization") == "Bearer TOKEN"
        if url.startswith(setup.UPLOAD):
            boundary = request.headers["Content-type"].split("boundary=")[1].encode()
            parts = body.split(b"--" + boundary)
            meta = json.loads(parts[1].split(b"\r\n\r\n", 1)[1].strip())
            data = parts[2].split(b"\r\n\r\n", 1)[1][:-2]
            fid = self.new(meta["name"], meta["parents"][0], "file"); self.blobs[fid] = data
            return FakeResponse(json.dumps({"id": fid, "size": str(len(data))}).encode())
        path = urllib.parse.urlsplit(url).path
        query = urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)
        if path.endswith("/permissions"):
            perms = [{"type": "user", "role": "owner"}] + ([{"type": "anyone", "role": "writer"}] if self.broad else [])
            return FakeResponse(json.dumps({"permissions": perms}).encode())
        if query.get("alt") == ["media"]:
            return FakeResponse(self.blobs[path.rsplit("/", 1)[1]])
        if method == "POST":
            meta = json.loads(body)
            return FakeResponse(json.dumps({"id": self.new(meta["name"], meta["parents"][0], meta["mimeType"])}).encode())
        q = query["q"][0]
        name = re.search(r"name = '([^']*)'", q).group(1); parent = re.search(r"'([^']*)' in parents", q).group(1)
        found = [{"id": i, "name": f["name"]} for i, f in self.files.items() if f["name"] == name and f["parent"] == parent]
        return FakeResponse(json.dumps({"files": found}).encode())

    def new(self, name, parent, mime):
        self.n += 1; fid = "id%d" % self.n
        self.files[fid] = {"name": name, "parent": parent, "mime": mime}
        return fid


class SetupTests(unittest.TestCase):
    def test_pkce_and_least_privilege_consent(self):
        verifier, challenge = setup.pkce()
        self.assertEqual(challenge, base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode())
        url = urllib.parse.parse_qs(urllib.parse.urlsplit(setup.consent_url("cid", "http://127.0.0.1:5000/", challenge, "st")).query)
        self.assertEqual(url["scope"], ["https://www.googleapis.com/auth/drive.file"])
        self.assertEqual((url["access_type"], url["code_challenge_method"], url["prompt"]), (["offline"], ["S256"], ["consent"]))

    def test_creates_private_folder_once_and_verifies_probe_readback(self):
        fake = FakeDriveRest(); drive = setup.Drive("TOKEN", fake)
        folder = drive.find_folder("S1_Research_Intake") or drive.create_folder("S1_Research_Intake")
        self.assertEqual(drive.find_folder("S1_Research_Intake"), folder)
        first = setup.verify(drive, folder); second = setup.verify(drive, folder)
        self.assertTrue(first["readback_matches"] and second["readback_matches"])
        self.assertEqual(first["folder_access"], "restricted")
        probes = [i for i, f in fake.files.items() if f["name"] == "_relay_probe"]
        self.assertEqual(len(probes), 1, "probe folder is reused, not recreated")

    def test_broadly_shared_folder_is_refused(self):
        fake = FakeDriveRest(broad=True); drive = setup.Drive("TOKEN", fake)
        with self.assertRaises(SystemExit):
            setup.verify(drive, drive.create_folder("S1_Research_Intake"))


if __name__ == "__main__":
    unittest.main()
