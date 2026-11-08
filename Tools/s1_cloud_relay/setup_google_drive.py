"""One-time owner setup for the relay's Google Drive destination (standard library only; run on the owner's PC).

  python setup_google_drive.py --client-secrets <oauth_client.json> [--folder-name S1_Research_Intake]
  python setup_google_drive.py --check            # re-verify the saved folder (Restricted, writable, SHA-256 readback)

1. Opens the owner's own browser for Google sign-in (loopback OAuth + PKCE). Scope drive.file only: the relay can see only the
   files/folders it creates, never the rest of the owner's Drive or the existing shared folder.
2. Creates (or reuses) a NEW folder in the owner's My Drive. New folders are private: the script refuses if any anyone/domain
   permission is present.
3. Uploads a small probe file, downloads it back by file ID and compares SHA-256.
4. Saves client_id/client_secret/refresh_token/folder_id to %USERPROFILE%\\.s1_relay\\drive_oauth.json (outside the repository).
   Nothing secret is printed. Put these values into the relay's server variables (see CLOUD_AUTO_UPLOAD_TH.md).
"""
import argparse, base64, hashlib, http.server, json, os, secrets, sys, threading, time, urllib.parse, urllib.request, webbrowser
from pathlib import Path

SCOPE = "https://www.googleapis.com/auth/drive.file"
API = "https://www.googleapis.com/drive/v3"
UPLOAD = "https://www.googleapis.com/upload/drive/v3/files"
STORE = Path(os.environ.get("USERPROFILE") or Path.home()) / ".s1_relay" / "drive_oauth.json"
FOLDER = "application/vnd.google-apps.folder"


def pkce():
    verifier = base64.urlsafe_b64encode(secrets.token_bytes(48)).rstrip(b"=").decode()
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    return verifier, challenge


def consent_url(client_id, redirect, challenge, state):
    return "https://accounts.google.com/o/oauth2/v2/auth?" + urllib.parse.urlencode(dict(
        client_id=client_id, redirect_uri=redirect, response_type="code", scope=SCOPE, access_type="offline",
        prompt="consent", code_challenge=challenge, code_challenge_method="S256", state=state))


def post_form(url, fields):
    data = urllib.parse.urlencode(fields).encode()
    with urllib.request.urlopen(urllib.request.Request(url, data=data, method="POST"), timeout=60) as r:
        return json.loads(r.read())


def sign_in(client):
    """Loopback flow: Google redirects the owner's browser to 127.0.0.1 on this PC; the code never leaves this machine."""
    verifier, challenge = pkce()
    state = secrets.token_urlsafe(24)
    result = {}

    class Catch(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            q = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
            result.update({k: v[0] for k, v in q.items()})
            self.send_response(200); self.send_header("Content-Type", "text/plain; charset=utf-8"); self.end_headers()
            self.wfile.write("S1 relay setup: you can close this tab.".encode())
        def log_message(self, *a): pass

    server = http.server.HTTPServer(("127.0.0.1", 0), Catch)
    redirect = "http://127.0.0.1:%d/" % server.server_address[1]
    threading.Thread(target=server.handle_request, daemon=True).start()
    url = consent_url(client["client_id"], redirect, challenge, state)
    print("Opening Google sign-in in your browser. If it does not open, paste this address into your browser:\n" + url, flush=True)
    webbrowser.open(url)
    deadline = time.time() + 300
    while "code" not in result and "error" not in result and time.time() < deadline:
        time.sleep(0.5)
    server.server_close()
    if result.get("state") != state or "code" not in result:
        raise SystemExit("Sign-in did not complete: " + str(result.get("error", "timeout")))
    token = post_form("https://oauth2.googleapis.com/token", dict(code=result["code"], client_id=client["client_id"],
                      client_secret=client["client_secret"], redirect_uri=redirect, grant_type="authorization_code", code_verifier=verifier))
    if "refresh_token" not in token:
        raise SystemExit("Google returned no refresh token (remove the app's earlier grant at myaccount.google.com/permissions and retry)")
    return token


def access_token(saved):
    return post_form("https://oauth2.googleapis.com/token", dict(client_id=saved["client_id"], client_secret=saved["client_secret"],
                     refresh_token=saved["refresh_token"], grant_type="refresh_token"))["access_token"]


class Drive:
    def __init__(self, token, opener=urllib.request.urlopen):
        self.token, self.open = token, opener

    def call(self, method, url, body=None, headers=None, raw=False):
        h = {"Authorization": "Bearer " + self.token}; h.update(headers or {})
        with self.open(urllib.request.Request(url, data=body, method=method, headers=h), timeout=60) as r:
            data = r.read()
        return data if raw else json.loads(data or b"{}")

    def find_folder(self, name, parent="root"):
        q = "name = '%s' and mimeType = '%s' and '%s' in parents and trashed = false" % (name.replace("'", "\\'"), FOLDER, parent)
        files = self.call("GET", API + "/files?" + urllib.parse.urlencode(dict(q=q, fields="files(id,name)")))["files"]
        if len(files) > 1:
            raise SystemExit("More than one folder named %r created by this app; keep one and rerun" % name)
        return files[0]["id"] if files else None

    def create_folder(self, name, parent="root"):
        body = json.dumps({"name": name, "mimeType": FOLDER, "parents": [parent]}).encode()
        return self.call("POST", API + "/files?fields=id", body, {"Content-Type": "application/json"})["id"]

    def broad_permissions(self, folder_id):
        perms = self.call("GET", API + "/files/%s/permissions?fields=permissions(type,role)" % folder_id)["permissions"]
        return sorted({p["type"] + "/" + p.get("role", "") for p in perms if p["type"] in ("anyone", "domain")})

    def upload(self, parent, name, data):
        boundary = "s1" + secrets.token_hex(8)
        meta = json.dumps({"name": name, "parents": [parent]})
        body = ("--%s\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n%s\r\n--%s\r\nContent-Type: application/octet-stream\r\n\r\n"
                % (boundary, meta, boundary)).encode() + data + ("\r\n--%s--\r\n" % boundary).encode()
        return self.call("POST", UPLOAD + "?uploadType=multipart&fields=id,size", body, {"Content-Type": "multipart/related; boundary=" + boundary})["id"]

    def download(self, file_id):
        return self.call("GET", API + "/files/%s?alt=media" % file_id, raw=True)


def verify(drive, folder_id):
    broad = drive.broad_permissions(folder_id)
    if broad:
        raise SystemExit("Folder is shared broadly (%s); make it Restricted in Google Drive and rerun --check" % ",".join(broad))
    probe_dir = drive.find_folder("_relay_probe", folder_id) or drive.create_folder("_relay_probe", folder_id)
    data = ("S1 relay setup probe %s\n" % time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())).encode()
    file_id = drive.upload(probe_dir, "probe_%d.txt" % int(time.time()), data)
    back = drive.download(file_id)
    ok = hashlib.sha256(back).hexdigest() == hashlib.sha256(data).hexdigest()
    return dict(folder_id=folder_id, folder_access="restricted", probe_file_id=file_id, probe_sha256=hashlib.sha256(data).hexdigest(),
                readback_matches=ok, link="https://drive.google.com/drive/folders/" + folder_id)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--client-secrets"); p.add_argument("--folder-name", default="S1_Research_Intake"); p.add_argument("--check", action="store_true")
    a = p.parse_args(argv)
    if a.check:
        saved = json.loads(STORE.read_text(encoding="utf-8"))
    else:
        if not a.client_secrets:
            raise SystemExit("--client-secrets <downloaded OAuth client JSON (Desktop app)> is required")
        client = json.loads(Path(a.client_secrets).read_text(encoding="utf-8"))
        client = client.get("installed") or client.get("web") or client
        token = sign_in(client)
        saved = dict(client_id=client["client_id"], client_secret=client["client_secret"], refresh_token=token["refresh_token"], scope=SCOPE)
        drive = Drive(token["access_token"])
        saved["folder_id"] = drive.find_folder(a.folder_name) or drive.create_folder(a.folder_name)
        saved["folder_name"] = a.folder_name
        STORE.parent.mkdir(parents=True, exist_ok=True)
        STORE.write_text(json.dumps(saved, indent=1), encoding="utf-8")
    result = verify(Drive(access_token(saved)), saved["folder_id"])
    result["secrets_saved_to"] = str(STORE)
    print(json.dumps(result, indent=1))
    return 0 if result["readback_matches"] else 2


if __name__ == "__main__":
    sys.exit(main())
