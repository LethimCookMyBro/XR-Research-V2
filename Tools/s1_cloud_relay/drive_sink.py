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
    h=hashlib.sha256()
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
            scopes=["https://www.googleapis.com/auth/drive.file"]),cache_discovery=False)
        self.parent=os.environ["S1_GOOGLE_FOLDER_ID"]
        self.access=self.confirm_restricted()

    def confirm_restricted(self):
        result=self.api.files().get(fileId=self.parent,fields="id,mimeType,capabilities(canAddChildren)",supportsAllDrives=True).execute()
        if result.get("mimeType")!="application/vnd.google-apps.folder" or not result.get("capabilities",{}).get("canAddChildren"):
            raise ValueError("Cloud target is not a writable Drive folder")
        # Google may restrict listing permissions if credentials lack share ownership. Fail closed.
        p=self.api.permissions().list(fileId=self.parent,fields="nextPageToken,permissions(type,role)",supportsAllDrives=True).execute()
        if p.get("nextPageToken"):
            raise PermissionError("Drive folder permissions could not be listed completely")
        broad=sorted({x.get("type")+"/"+x.get("role","") for x in p.get("permissions",[]) if x.get("type") in ("anyone","domain")})
        if not broad:
            return "restricted"
        # Owner keeps "anyone with link / editor" on purpose (28 Oct). That never admits study data; an explicit, recorded
        # owner acknowledgement admits DEV/TEST/PILOT bundles only. Without it the sink stays closed.
        if not os.environ.get("S1_GOOGLE_BROAD_FOLDER_ACK"):
            raise PermissionError("Drive folder is shared broadly ("+",".join(broad)+"); configure a Restricted intake folder or an owner acknowledgement for DEV data")
        return "broad_acknowledged:"+",".join(broad)

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

    def readback(self,file_id):
        """SHA-256 of the bytes Google returns for this file ID (server-side check, not a local cache)."""
        from googleapiclient.http import MediaIoBaseDownload
        import io
        h=hashlib.sha256();buf=io.BytesIO();size=0
        down=MediaIoBaseDownload(buf,self.api.files().get_media(fileId=file_id,supportsAllDrives=True),chunksize=8*1024*1024)
        done=False
        while not done:
            _,done=down.next_chunk(num_retries=2)
            data=buf.getvalue();h.update(data);size+=len(data);buf.seek(0);buf.truncate()
        return size,h.hexdigest()

    def file(self,parent,name,path):
        from googleapiclient.http import MediaFileUpload
        path=Path(path)
        size=path.stat().st_size;expected=digest(path)
        existing=self.find(parent,name)
        if existing:
            file_id=existing["id"]
            if int(existing.get("size",-1))!=size:
                raise ValueError("Remote file conflict " + name)
        else:
            upload=MediaFileUpload(str(path),mimetype="application/octet-stream",resumable=True,chunksize=8*1024*1024)
            request=self.api.files().create(body={"name":name,"parents":[parent]},media_body=upload,
                                            fields="id,size",supportsAllDrives=True)
            response=None
            while response is None:
                _,response=request.next_chunk(num_retries=2)
            file_id=response["id"]
        got_size,got=self.readback(file_id)
        if got_size!=size or got!=expected:
            raise ValueError("Drive bytes differ from relay copy " + name)
        return {"id":file_id,"bytes":size,"sha256":got}

    def sync(self,bundle):
        base=Path(bundle)
        m=json.loads((base/"manifest.json").read_text(encoding="utf-8"))
        i=m["identity"]
        if self.access!="restricted" and i.get("test_data") is not True:
            raise PermissionError("Study bundles are never written to a broadly shared folder")
        device=json.loads((base/"relay_device.json").read_text(encoding="utf-8")) if (base/"relay_device.json").exists() else {}
        device_id=device.get("device_id") or "legacy"
        group=i["group"];cohort="TH" if group=="Thai" else "JP" if group=="Japanese" else group.upper()
        # Same tree as the PC route (Tools/research_cloud_sync.py remote_dir with a pinned folder ID): no extra research_s1 level.
        path=["site_"+os.environ.get("S1_SITE_CODE","TMU"),"device_"+device_id,"cohort_"+cohort,"participant_"+i["participant_code"],
              "session_"+i["session_id"],"run_"+i["run_id"],"bundle_"+m["bundle_id"]]
        folder=self.parent
        for name in path:
            folder=self.folder(folder,name)
        uploaded={}
        raw_folder=self.folder(folder,"raw")
        for name in sorted([*m["files"],"manifest.json","COMPLETE.json"]):
            file=base/name
            destination=raw_folder if name.startswith("raw/") else folder
            uploaded[name]=self.file(destination,name[4:] if destination==raw_folder else name,file)
            if name in m["files"] and uploaded[name]["sha256"]!=m["files"][name]["sha256"]:
                raise ValueError("Drive bytes differ from device manifest " + name)
        return dict(schema_version="s1.drive-receipt.2",bundle_id=m["bundle_id"],
                    manifest_sha256=hashlib.sha256((base/"manifest.json").read_bytes()).hexdigest(),
                    device_id=device_id,device_label=device.get("device_label"),
                    status="provider_verified",provider_upload_confirmed=True,
                    target_folder_id=self.parent,folder_access=self.access,destination="/".join(path),
                    verified_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ",time.gmtime()),
                    verification="sha256 of bytes downloaded from Google Drive by file ID",
                    provider_files=uploaded)


def _write(receipt,body):
    temp=receipt.with_suffix(".next")
    temp.write_text(json.dumps(body,sort_keys=True),encoding="utf-8")
    os.replace(temp,receipt)


def sync_pending(store,sink):
    """One pass over every device namespace. Failures keep the relay copy and record why, then retry next pass."""
    receipts=sorted([*store.root.glob("*/relay_receipt.json"),*store.root.glob("device_*/*/relay_receipt.json")])
    for receipt in receipts:
        body=json.loads(receipt.read_text(encoding="utf-8"))
        if body.get("status") in ("provider_verified","drive_verified"):continue
        try:
            cloud=sink.sync(receipt.parent)
            _write(receipt,dict(body,**cloud,pending_reason=None))
        except Exception as ex:
            _write(receipt,dict(body,status="staged_verified",provider_upload_confirmed=False,
                                pending_reason=type(ex).__name__+": "+str(ex)[:160],last_attempt_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ",time.gmtime())))
            print("[S1Drive] Pending: "+type(ex).__name__,flush=True)
    return receipts


def sync_forever(store,period=90):
    """Only emits boolean capability/state, never secret/participant identity."""
    while True:
        if ready():
            try:
                with _DRIVE_LOCK:
                    sink=DriveSink()
                    sync_pending(store,sink)
            except Exception as ex:
                print("[S1Drive] Disabled by target/auth check: "+type(ex).__name__,flush=True)
        time.sleep(period)
