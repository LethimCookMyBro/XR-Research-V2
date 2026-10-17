"""Exercise actual HTTP transport from a synthetic 'Quest' using stdlib only."""
import base64,hashlib,json,os,tempfile,threading,unittest,urllib.request,urllib.error
from http.server import ThreadingHTTPServer
from receiver import Handler,Store,sha,MAX_CHUNK

TOKEN="DEV_ONLY_TEST_TOKEN_MUST_NOT_BE_USED_IN_PRODUCTION_123"
def payload():
    sources={
      "raw/events.jsonl":b'{"event":"DEV_SYNTHETIC"}\n'*8000,
      "raw/movement.csv":b"timestamp,valid\n"+b"0.0,true\n"*65000,
      "raw/session_metadata.json":b'{"run_id":"run_mock","technical_test":true}',
      "raw/research-session.json":b'{"participant_id":"DEV321","run_id":"run_mock"}'
    }
    manifest=dict(schema_version="researchflow.device-bundle.1",bundle_id="snapshot_mock000000000000000000",
      identity=dict(participant_code="DEV321",session_id="S001",run_id="run_mock",group="Dev",test_data=True),
      files={name:dict(bytes=len(data),sha256=sha(data)) for name,data in sources.items()})
    raw=json.dumps(manifest,separators=(",",":")).encode()
    complete=json.dumps(dict(schema_version="researchflow.bundle-complete.1",bundle_id=manifest["bundle_id"],manifest_sha256=sha(raw))).encode()
    return manifest,sources,dict(manifest_b64=base64.b64encode(raw).decode(),complete_b64=base64.b64encode(complete).decode())

class HttpRoundTrip(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.old=os.environ.get("S1_RELAY_TOKEN_SHA256")
        os.environ["S1_RELAY_TOKEN_SHA256"]=sha(TOKEN.encode())
        Handler.store=Store(self.tmp.name)
        self.server=ThreadingHTTPServer(("127.0.0.1",0),Handler)
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
        self.url=f"http://127.0.0.1:{self.server.server_port}/v1/bundles"
    def tearDown(self):
        self.server.shutdown();self.server.server_close();self.thread.join(timeout=3)
        if self.old is None:os.environ.pop("S1_RELAY_TOKEN_SHA256",None)
        else:os.environ["S1_RELAY_TOKEN_SHA256"]=self.old
    def call(self,method,path="",body=None,headers=None):
        req=urllib.request.Request(self.url+path,method=method,data=body,
            headers={"Authorization":"Bearer "+TOKEN,**(headers or {})})
        with urllib.request.urlopen(req,timeout=10) as r:
            return json.load(r)
    def test_end_to_end_resume_and_unverified_drive(self):
        m,sources,init=payload();id=m["bundle_id"]
        state=self.call("POST",body=json.dumps(init).encode(),headers={"Content-Type":"application/json"})
        self.assertEqual(state["offsets"]["raw/events.jsonl"],0)
        for name,data in sorted(sources.items()):
            # Split at arbitrary 11kiB to exercise many resumable chunks.
            for off in range(0,len(data),11*1024):
                chunk=data[off:off+11*1024]
                headers={"X-File":name,"X-Offset":str(off),"X-Chunk-SHA256":sha(chunk)}
                r=self.call("PUT","/"+id+"/chunk",chunk,headers)
                self.assertEqual(r["next_offset"],off+len(chunk))
                if off==0:
                    # duplicate first chunk after receiving success must not append.
                    self.assertEqual(self.call("PUT","/"+id+"/chunk",chunk,headers)["next_offset"],off+len(chunk))
        self.assertEqual(self.call("GET","/"+id)["offsets"]["raw/movement.csv"],len(sources["raw/movement.csv"]))
        receipt=self.call("POST","/"+id+"/finalize",b"")
        self.assertEqual(receipt["state"],"staged_verified")
        self.assertFalse(receipt["provider_upload_confirmed"])
        self.assertEqual(self.call("POST","/"+id+"/finalize",b"")["state"],"staged_verified")
        for name,data in sources.items():
            self.assertEqual((Handler.store.folder(id)/name).read_bytes(),data)
        self.assertEqual(sha((Handler.store.folder(id)/"manifest.json").read_bytes()),json.loads((Handler.store.folder(id)/"COMPLETE.json").read_bytes())["manifest_sha256"])

if __name__=="__main__":unittest.main()
