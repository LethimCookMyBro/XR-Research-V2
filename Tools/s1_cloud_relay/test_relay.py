"""Synthetic-only protocol tests. Loopback network only; no Google Drive or participant data."""
import base64,hashlib,http.client,json,os,socket,tempfile,threading,time,unittest,urllib.error,urllib.request
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

    def test_health_auth_configuration_consistency(self):
        names = ("S1_RELAY_TOKEN_SHA256", "S1_RELAY_DEVICE_TOKENS_JSON")
        old = {name: os.environ.get(name) for name in names}
        token = "FixtureOnlyHealthMatrix12345678901234567890"
        hashed = sha(token.encode())
        cases = [
            (None, None, False, 401),
            (hashed, None, True, 200),
            ("invalid", None, False, 401),
            (None, json.dumps({"DEV_HEALTH": hashed}), True, 200),
            (None, json.dumps({"DEV_HEALTH": ""}), False, 401),
            (None, "{}", False, 401),
            (None, "{", False, 401),
            (None, "[]", False, 401),
            (None, json.dumps({"DEV_HEALTH": 123}), False, 401),
            (None, json.dumps({"OTHER_DEVICE": hashed}), True, 401),
            (None, json.dumps({"INVALID/DEVICE": hashed}), False, 401),
            (hashed, "{}", False, 401),
        ]
        Handler.store = self.store
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        url = f"http://127.0.0.1:{server.server_port}"
        try:
            for legacy, registry, enabled, status in cases:
                for name, value in zip(names, (legacy, registry)):
                    if value is None:
                        os.environ.pop(name, None)
                    else:
                        os.environ[name] = value
                with urllib.request.urlopen(url + "/health", timeout=3) as response:
                    self.assertEqual(json.load(response)["upload_enabled"], enabled)
                body = json.dumps(payload()[2]).encode() if status == 200 else b""
                request = urllib.request.Request(url + "/v1/bundles", method="POST", data=body,
                    headers={"Authorization": "Bearer " + token, "X-Device-Id": "DEV_HEALTH"})
                try:
                    with urllib.request.urlopen(request, timeout=3) as response:
                        observed = response.status
                except urllib.error.HTTPError as error:
                    observed = error.code
                self.assertEqual(observed, status)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)
            for name, value in old.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_unauthorized_response_survives_delayed_request_body(self):
        Handler.store = self.store
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with socket.create_connection(server.server_address, timeout=3) as client:
                client.sendall(b"POST /v1/bundles HTTP/1.1\r\nHost: localhost\r\nContent-Length: 149999\r\nConnection: close\r\n\r\n")
                headers = bytearray()
                for _ in range(8192):
                    byte = client.recv(1)
                    self.assertTrue(byte, "Connection ended before response headers")
                    headers.extend(byte)
                    if headers.endswith(b"\r\n\r\n"):
                        break
                self.assertTrue(headers.startswith(b"HTTP/1.0 401 "))
                self.assertTrue(headers.endswith(b"\r\n\r\n"))
                client.sendall(b"x" * 149999)
                body = bytearray()
                for _ in range(24):
                    byte = client.recv(24 - len(body))
                    if not byte:
                        break
                    body.extend(byte)
                    if len(body) == 24:
                        break
                self.assertEqual(body, b'{"error":"unauthorized"}')
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

    def test_unauthorized_slow_body_has_absolute_cleanup_deadline(self):
        Handler.store = self.store
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with socket.create_connection(server.server_address, timeout=3) as client:
                client.sendall(b"POST /v1/bundles HTTP/1.1\r\nHost: localhost\r\nContent-Length: 149999\r\nConnection: close\r\n\r\n")
                response = http.client.HTTPResponse(client)
                response.begin()
                self.assertEqual(response.status, 401)
                self.assertEqual(response.read(), b'{"error":"unauthorized"}')
                client.settimeout(.05)
                start = time.monotonic()
                closed = False
                for _ in range(15):
                    try:
                        client.sendall(b"x")
                        time.sleep(.2)
                        if client.recv(1) == b"":
                            closed = True
                            break
                    except socket.timeout:
                        continue
                    except OSError:
                        closed = True
                        break
                self.assertTrue(closed, "Slow progress retained an unauthorized connection")
                self.assertLess(time.monotonic() - start, 2)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

    def test_unauthorized_malformed_and_oversized_lengths_still_deny(self):
        Handler.store = self.store
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            for length in ("bad", "-1", "150001", "99999999999999999999999999"):
                with self.subTest(length=length), socket.create_connection(server.server_address, timeout=3) as client:
                    client.sendall(("POST /v1/bundles HTTP/1.1\r\nHost: localhost\r\nContent-Length: " + length + "\r\nConnection: close\r\n\r\n").encode())
                    response = http.client.HTTPResponse(client)
                    response.begin()
                    self.assertEqual(response.status, 401)
                    self.assertEqual(response.read(), b'{"error":"unauthorized"}')
            self.assertEqual(list(Path(self.tmp.name).iterdir()), [])
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

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
