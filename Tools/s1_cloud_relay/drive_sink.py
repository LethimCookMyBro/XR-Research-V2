"""Google Drive backend for the S1 relay. Starts ONLY with explicit server-side OAuth approval/config.

OAuth refresh token belongs to approved researcher account and MUST only exist in server secrets.
Never expose tokens, Drive file IDs or participant paths through public logs.
"""
import hashlib, json, os, threading, time
from pathlib import Path

_DRIVE_LOCK = threading.Lock()


def ready():
    names=("S1_GOOGLE_CLIENT_ID","S1_GOOGLE_CLIENT_SECRET","S1_GOOGLE_REFRESH_TOKEN","S1_GOOGLE_FOLDER_ID")
    return os.environ.get("S1_GOOGLE_ENABLED")=="true" and all(os.environ.get(x) for x in names)


def digest(path):
    h=hashlib.md5()
    with Path(path).open("rb") as f:
        for buf in iter(lambda:f.read(1024*1024),b""):h.update(buf)
    return h.hexdigest()


class DriveSink:
    def __init__(self):
        from google.oauth2.credentials import Credentials
        from googleapiclient.discovery import build
        self.api=build("drive","v3",credentials=Credentials(
            token=None,refresh_token=os.environ["S1_GOOGLE_REFRESH_TOKEN"],
            token_uri="https://oauth2.googleapis.com/token",
            client_id=os.environ["S1_GOOGLE_CLIENT_ID"],
            client_secret=os.environ["S1_GOOGLE_CLIENT_SECRET"],
            scopes=["https://www.googleapis.com/auth/drive"]),cache_discovery=False)
        self.parent=os.environ["S1_GOOGLE_FOLDER_ID"]
        self.confirm_restricted()

    def confirm_restricted(self):
        result=self.api.files().get(fileId=self.parent,fields="id,mimeType,capabilities(canAddChildren)",supportsAllDrives=True).execute()
        if result.get("mimeType")!="application/vnd.google-apps.folder" or not result.get("capabilities",{}).get("canAddChildren"):
            raise ValueError("Cloud target is not a writable Drive folder")
        # Google may restrict listing permissions if credentials lack share ownership. Fail closed.
        p=self.api.permissions().list(fileId=self.parent,fields="nextPageToken,permissions(type,role)",supportsAllDrives=True).execute()
        if p.get("nextPageToken") or any(x.get("type") in ("anyone","domain") for x in p.get("permissions",[])):
            raise PermissionError("Drive folder must be Restricted, with no public/domain access")

    def find(self,parent,name,mime=None):
        q="'"+parent.replace("'","\\'")+"' in parents and name = '"+name.replace("'","\\'")+"' and trashed = false"
        if mime:q+=" and mimeType = '"+mime+"'"
        r=self.api.files().list(q=q,fields="nextPageToken,files(id,name,size,md5Checksum,mimeType)",pageSize=100,supportsAllDrives=True,includeItemsFromAllDrives=True).execute()
        if r.get("nextPageToken") or len(r.get("files",[]))>1:
            raise ValueError("Ambiguous duplicate Drive names, refusing overwrite")
        return (r.get("files") or [None])[0]

    def folder(self,parent,name):
        mime="application/vnd.google-apps.folder"
        existing=self.find(parent,name,mime)
        if existing:return existing["id"]
        r=self.api.files().create(body={"name":name,"mimeType":mime,"parents":[parent]},
                                  fields="id",supportsAllDrives=True).execute()
        return r["id"]

    def file(self,parent,name,path):
        from googleapiclient.http import MediaFileUpload
        path=Path(path)
        size=path.stat().st_size;md5=digest(path)
        existing=self.find(parent,name)
        if existing:
            if int(existing.get("size",-1))!=size or existing.get("md5Checksum")!=md5:
                raise ValueError("Remote file conflict " + name)
            return existing["id"]
        upload=MediaFileUpload(str(path),mimetype="application/octet-stream",resumable=True,chunksize=8*1024*1024)
        request=self.api.files().create(body={"name":name,"parents":[parent]},media_body=upload,
                                        fields="id,size,md5Checksum",supportsAllDrives=True)
        response=None
        while response is None:
            _,response=request.next_chunk(num_retries=2)
        if int(response.get("size",-1))!=size or response.get("md5Checksum")!=md5:
            raise ValueError("Drive did not verify uploaded bytes")
        return response["id"]

    def sync(self,bundle):
        base=Path(bundle)
        m=json.loads((base/"manifest.json").read_text(encoding="utf-8"))
        i=m["identity"]
        group={"Dev":"DEV","Test":"TEST","Pilot":"PILOT"}[i["group"]]
        folder=self.parent
        path=["research_s1","site_TMU","cohort_"+group,"participant_"+i["participant_code"],
              "session_"+i["session_id"],"run_"+i["run_id"],"bundle_"+m["bundle_id"]]
        for name in path:
            folder=self.folder(folder,name)
        uploaded={}
        raw_folder=self.folder(folder,"raw")
        for name in sorted([*m["files"],"manifest.json","COMPLETE.json"]):
            file=base/name
            destination=raw_folder if name.startswith("raw/") else folder
            uploaded[name]=self.file(destination,name[4:] if destination==raw_folder else name,file)
        return dict(schema_version="s1.drive-receipt.1",bundle_id=m["bundle_id"],
                    manifest_sha256=hashlib.sha256((base/"manifest.json").read_bytes()).hexdigest(),
                    status="drive_verified",provider_upload_confirmed=True,
                    target="google_drive_restricted",destination= "/".join(path),
                    provider_file_count=len(uploaded))


def sync_forever(store,period=90):
    """Only emits boolean capability/state, never secret/participant identity."""
    while True:
        if ready():
            try:
                with _DRIVE_LOCK:
                    sink=DriveSink()
                    for receipt in sorted(store.root.glob("*/relay_receipt.json")):
                        body=json.loads(receipt.read_text(encoding="utf-8"))
                        if body.get("status")=="drive_verified":continue
                        try:
                            cloud=sink.sync(receipt.parent)
                            temp=receipt.with_suffix(".next")
                            temp.write_text(json.dumps(cloud,sort_keys=True),encoding="utf-8")
                            os.replace(temp,receipt)
                        except Exception as ex:
                            print("[S1Drive] Pending: "+type(ex).__name__,flush=True)
            except Exception as ex:
                print("[S1Drive] Disabled by target/auth check: "+type(ex).__name__,flush=True)
        time.sleep(period)
