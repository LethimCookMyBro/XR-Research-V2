"""Live check of the deployed relay with two SIMULATED Quests and synthetic DEV bundles (never participant data).

  python simulate_quests.py [--endpoint https://...] [--wait 240]

Uses the install key from %USERPROFILE%\\.s1_relay\\enroll_key.json. Simulators enroll as devsim... and get SIM_NN folders, so real
headsets keep Quest_01, Quest_02, ... Both upload the same participant code at the same time; SIM B drops after 3 chunks and
resumes. Exits 0 only when both bundles report provider_verified (bucket readback SHA-256 matched).
"""
import argparse, base64, hashlib, json, os, sys, threading, time, urllib.error, urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_multidevice import bundle

CHUNK = 256 * 1024


def call(url, method, body=b"", token="", device="", headers=None):
    h = {"Authorization": "Bearer " + token, "Content-Type": "application/json"}
    if device: h["X-Device-Id"] = device
    h.update(headers or {})
    try:
        with urllib.request.urlopen(urllib.request.Request(url, data=body, method=method, headers=h), timeout=40) as r:
            return r.status, json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


def run(endpoint, install, token, mb, cb, files, stop_after=None):
    bid = json.loads(mb)["bundle_id"]
    init = json.dumps({"manifest_b64": base64.b64encode(mb).decode(), "complete_b64": base64.b64encode(cb).decode()}).encode()
    status, answer = call(endpoint + "/v1/bundles", "POST", init, token, install)
    if status != 200: return status, answer
    sent = 0
    for name in sorted(files):
        offset = answer["offsets"][name]
        while offset < len(files[name]):
            if stop_after is not None and sent >= stop_after: return 0, {"state": "network_dropped"}
            block = files[name][offset:offset + CHUNK]
            status, reply = call(endpoint + "/v1/bundles/" + bid + "/chunk", "PUT", block, token, install,
                                 {"Content-Type": "application/octet-stream", "X-File": name, "X-Offset": str(offset),
                                  "X-Chunk-SHA256": hashlib.sha256(block).hexdigest()})
            if status != 200: return status, reply
            offset = reply["next_offset"]; sent += 1
    return call(endpoint + "/v1/bundles/" + bid + "/finalize", "POST", b"", token, install)


def main():
    p = argparse.ArgumentParser(); p.add_argument("--endpoint", default="https://s1-quest-relay-production.up.railway.app"); p.add_argument("--wait", type=int, default=240)
    a = p.parse_args()
    key = json.loads((Path(os.environ.get("USERPROFILE") or Path.home()) / ".s1_relay" / "enroll_key.json").read_text(encoding="utf-8"))["enroll_key"]
    stamp = time.strftime("%Y%m%d%H%M%S", time.gmtime())
    sims = {}
    for tag in ("a", "b"):
        install = "devsim" + hashlib.sha256((tag + stamp).encode()).hexdigest()[:26]
        status, e = call(a.endpoint + "/v1/enroll", "POST", json.dumps({"install_id": install}).encode(), key)
        assert status == 200, (status, e)
        sims[tag] = (install, e["device_token"], e["label"])
        print("enrolled", tag, e["label"])
    # Same participant code and bundle ID on both simulators, different bytes.
    payloads = {tag: bundle(code="DEV001", session="s_sim" + stamp, bundle_id="snapshot_sim" + stamp + "abcdef", seed=tag.encode()) for tag in sims}
    results = {}
    def go(tag, stop=None):
        install, token, _ = sims[tag]; results[tag] = run(a.endpoint, install, token, *payloads[tag], stop_after=stop)
    threads = [threading.Thread(target=go, args=("a",)), threading.Thread(target=go, args=("b", 3))]
    [t.start() for t in threads]; [t.join() for t in threads]
    print("first pass", {t: r[1].get("state") for t, r in results.items()})
    go("b")  # SIM B comes back online and resumes from the relay offsets
    print("after resume", {t: r[1].get("state") for t, r in results.items()})
    bid = json.loads(payloads["a"][0])["bundle_id"]
    deadline = time.time() + a.wait
    while time.time() < deadline:
        states = {t: call(a.endpoint + "/v1/bundles/" + bid, "GET", None, sims[t][1], sims[t][0])[1] for t in sims}
        if all(s.get("state") == "provider_verified" and s.get("provider_upload_confirmed") for s in states.values()):
            for t in sims: print("verified", sims[t][2], "bundle", bid)
            return 0
        time.sleep(15)
    print("NOT verified in time:", {t: (s.get("state"), s.get("pending_reason")) for t, s in states.items()})
    return 2


if __name__ == "__main__":
    sys.exit(main())
