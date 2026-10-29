"""Synthetic build fixtures; no Unity, native input or participant data."""
import json
import tempfile
from pathlib import Path

import build


def run():
    with tempfile.TemporaryDirectory() as directory:
        lf, crlf = (Path(directory) / name for name in ("lf.txt", "crlf.txt"))
        lf.write_bytes("Thai ไทย / 日本語\nsecond line\n".encode("utf-8"))
        crlf.write_bytes(lf.read_bytes().replace(b"\n", b"\r\n"))
        assert lf.read_bytes() != crlf.read_bytes()
        assert build.version_digest([lf]) == build.version_digest([crlf])
        crlf.write_bytes(crlf.read_bytes().replace(b"second", b"changed"))
        assert build.version_digest([lf]) != build.version_digest([crlf])
    frames = [{"anchors": {"moving": [i * 10, 30, True], "fixed": [50, 30, True]}} for i in range(3)]
    callouts = [{"anchor": "moving"}, {"anchor": "fixed"}]
    scene = {"callouts": callouts, "motion_windows": [[0, 0], [0, 1], [1, 1], [.25, .25]]}
    assert build.video_sentence_anchors(frames, scene, 0) == (frames[0], callouts)
    assert build.video_sentence_anchors(frames, scene, 1) == (frames[0], [callouts[1]])
    assert build.video_sentence_anchors(frames, scene, 2) == (frames[-1], callouts)
    assert build.video_sentence_anchors(frames, scene, 3)[0] is frames[1]  # Encoder uses half-up, not bankers rounding.
    frames[-1]["anchors"]["fixed"][2] = False
    assert build.video_sentence_anchors(frames, scene, 1)[1] == []
    for version in ("v2", "v3"):
        content = build.load(version)
        assert not build.check(content)[0], build.check(content)[0]
    content = build.load()
    scene = next(s for s in content["A"]["video"] if s.get("motion_windows"))
    scene["motion_windows"] = [[1, 0]] * len(scene["say"]["th"])
    assert any("invalid sentence-motion windows" in e for e in build.check(content)[0])
    windows, starts, ends = [[0, 0], [0, 1], [1, 1]], [1, 3, 6], [2, 5, 7]
    for time, expected in ((0, 0), (2.5, 0), (3, 0), (4, .5), (5.5, 1), (8, 1)):
        assert build.motion_position(windows, starts, ends, time, 8) == expected

    assert build.illustration_size({"layout": "pair"}) == (1280, 520)
    assert build.illustration_size({"layout": "triple"}) == (1280, 460)
    assert build.illustration_size({"layout": "pair", "card": {"title": "fixture"}}) == (1280, 720)
    assert build.illustration_size({"layout": "single"}) == (1280, 720)
    original = build.ROOT, build.RES, build.STREAM
    try:
        with tempfile.TemporaryDirectory() as directory:
            capture = Path(directory) / "capture"
            (capture / "T_station").mkdir(parents=True)
            (capture / "shots.json").write_text(json.dumps({"T_station": {"task": "T", "frames": [{"file": "0000.png"}]}}))
            pixels = capture / "T_station/0000.png"
            pixels.write_bytes(b"synthetic frame hash fixture")
            old = build.capture_digest(capture, "T")
            pixels.write_bytes(b"changed synthetic pixels")
            assert old != build.capture_digest(capture, "T")
            manifest_path = capture / "shots.json"
            metadata = json.loads(manifest_path.read_text())
            before_metadata = build.capture_digest(capture, "T")
            metadata["T_station"]["frames"][0]["anchors"] = {"module": [20, 20, True]}
            manifest_path.write_text(json.dumps(metadata))
            assert before_metadata != build.capture_digest(capture, "T")
            before_metadata = build.capture_digest(capture, "T")
            metadata["T_station"]["frames"][0]["anchors"]["module"][0] += 1
            manifest_path.write_text(json.dumps(metadata))
            assert before_metadata != build.capture_digest(capture, "T")

            try:
                build.capture_digest(capture, "A")
                raise AssertionError("missing capture task accepted")
            except ValueError:
                pass
            (Path(directory) / "outside.bin").write_bytes(b"synthetic outside-path fixture")
            (capture / "shots.json").write_text(json.dumps({"T_station": {"task": "T", "frames": [{"file": "../../outside.bin"}]}}))
            try:
                build.capture_digest(capture, "T")
                raise AssertionError("capture path traversal accepted")
            except ValueError:
                pass
            build.ROOT = Path(directory)
            build.RES = build.ROOT / "Assets/Resources"
            build.STREAM = build.ROOT / "Assets/StreamingAssets"
            build.STREAM.mkdir(parents=True)
            package = build.RES / "A_th"
            (package / "pages").mkdir(parents=True)
            for name in ("source_pdf.bytes", "pages/p01.bytes", "pages/p01.bytes.meta"):
                (package / name).write_bytes(b"synthetic accepted manual fixture")
            before = {p: p.read_bytes() for p in package.rglob("*") if p.is_file()}
            previous = {"source_pdf": "accepted.pdf", "source_pdf_resource": "accepted/resource",
                        "source_sha256": build.sha(package / "source_pdf.bytes"), "pages": [{"page": 1}]}
            (package / "package.json").write_text(json.dumps(previous))
            movie = build.ROOT / "synthetic-movie.bin"
            movie.write_bytes(b"not a real video; hash fixture only")
            built = {"pages": [], "pdf": None, "images": [], "video": movie,
                     "steps": ["One action"], "duration": 1, "chapters": []}
            result = build.install("A", "th", {}, built, "fixture-v3", keep_manual=True)
            assert b"\r\n" not in (package / "package.json").read_bytes()
            assert all(result[key] == value for key, value in previous.items())
            assert all(path.read_bytes() == value for path, value in before.items())
            assert result["steps"] == [{"text_th": "One action"}]
    finally:
        build.ROOT, build.RES, build.STREAM = original
    print("PASS: versions, sentence-motion windows, accepted PDF/page bytes preserved")


if __name__ == "__main__":
    run()
