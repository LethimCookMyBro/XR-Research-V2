"""Synthetic-only protocol tests. No network, Google Drive or participant data."""
import base64,hashlib,json,os,tempfile,threading,unittest,urllib.error,urllib.request
from pathlib import Path
from http.server import ThreadingHTTPServer
from receiver import Store,Handler,sha,FILES


def payload(test_data=True):
    ident={"participant_code":"DEV321" if test_data else "TH321",
           "session_id":"session_123","run_id":"run_123",
           "group":"Dev" if test_data else "Thai","test_data":test_data}
    files={name:(name+"...").encode()*5 for name in FILES}
    meta={name:{"bytes":len(buf),"sha256":sha(buf)} for name,buf in files.items()}
    m={"schema_version":"researchflow.device-bundle.1","bundle_id":"snapshot_deadbeef0123456789012345",
       "identity":ident,"files":meta}
    mb=json.dumps(m,sort_keys=True).encode()
    c={"schema_version":"researchflow.bundle-complete.1","bundle_id":m["bundle_id"],"manifest_sha256":sha(mb)}
    cb=json.dumps(c,sort_keys=True).encode()
    return m,files,{"manifest_b64":base64.b64encode(mb).decode(),"complete_b64":base64.b64encode(cb).decode()}


class StoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store=Store(self.tmp.name)

    def test_restart_resume_retry_hash_and_idempotence(self):
        manifest,files,req=payload()
        self.store.request(req)
        id=manifest["bundle_id"]
        name="raw/events.jsonl"
        allbytes=files[name]
        one=allbytes[:13]
        self.assertEqual(self.store.chunk(id,name,0,one,sha(one))["next_offset"],13)
        self.assertEqual(self.store.chunk(id,name,0,one,sha(one))["next_offset"],13)
        with self.assertRaises(ValueError):
            self.store.chunk(id,name,0,b"WRONG",sha(b"WRONG"))
        self.assertEqual(self.store.status(id)["offsets"][name],13)
        restored=Store(self.tmp.name)
        self.assertEqual(restored.status(id)["offsets"][name],13)
        tail=allbytes[13:]
        restored.chunk(id,name,13,tail,sha(tail))
        for other in sorted(FILES-{name}):
            restored.chunk(id,other,0,files[other],sha(files[other]))
        self.assertEqual(restored.finish(id)["state"],"staged_verified")
        self.assertFalse(restored.finish(id)["provider_upload_confirmed"])
        self.assertEqual(restored.finish(id)["state"],"staged_verified")
        self.assertEqual((restored.folder(id)/name).read_bytes(),allbytes)

    def test_auth_gates_and_malformed_bundles(self):
        _,_,technical=payload()
        self.store.request(technical)
        self.store.request(technical)
        _,_,study=payload(False)
        with self.assertRaises(PermissionError):
            self.store.request(study)
        b=dict(technical)
        b["manifest_b64"]=base64.b64encode(b"{}").decode()
        with self.assertRaises((ValueError,KeyError)):
            self.store.request(b)

    def test_chunk_offsets_and_sha(self):
        m,files,p=payload()
        self.store.request(p)
        id=m["bundle_id"]
        name="raw/events.jsonl"
        chunk=files[name]
        self.assertEqual(self.store.chunk(id,name,300,chunk,sha(chunk))["next_offset"],0)
        with self.assertRaises(ValueError):
            self.store.chunk(id,name,0,chunk,"0"*64)
        with self.assertRaises(ValueError):
            self.store.chunk(id,"../session",0,chunk,sha(chunk))
        with self.assertRaises(ValueError):
            self.store.finish(id)

    def test_http_bearer(self):
        m,files,p=payload()
        old=os.environ.get("S1_RELAY_TOKEN_SHA256")
        os.environ["S1_RELAY_TOKEN_SHA256"]=sha(b"AValidPerDeviceSecret1234567890123456789")
        Handler.store=self.store
        server=ThreadingHTTPServer(("127.0.0.1",0),Handler)
        server.timeout=2
        thread=threading.Thread(target=server.serve_forever,daemon=True)
        thread.start()
        try:
            url=f"http://127.0.0.1:{server.server_port}/v1/bundles"
            def request(auth):
                req=urllib.request.Request(url,method="POST",data=json.dumps(p).encode(),
                                           headers={"Content-Type":"application/json"})
                if auth:req.add_header("Authorization","Bearer "+auth)
                return urllib.request.urlopen(req,timeout=3)
            with self.assertRaises(urllib.error.HTTPError) as error:
                request(None)
            self.assertEqual(error.exception.code,401)
            with request("AValidPerDeviceSecret1234567890123456789") as result:
                self.assertEqual(json.load(result)["bundle_id"],m["bundle_id"])
        finally:
            server.shutdown();server.server_close();thread.join(timeout=3)
            if old is None:os.environ.pop("S1_RELAY_TOKEN_SHA256",None)
            else:os.environ["S1_RELAY_TOKEN_SHA256"]=old


if __name__=="__main__":
    unittest.main()
