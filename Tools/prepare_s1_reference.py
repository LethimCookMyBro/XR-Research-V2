import argparse
from dataclasses import dataclass
from datetime import datetime
import hashlib
import json
import math
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from PIL import Image


CONTENT_SHA = "477301a25e4135560e94bd732200b08e221da4a43a871416779a6a5a9eed7808"
REVIEWED_MANIFEST_SHA = "35556db8f25d30cea4d62738f5a5ed7994a8d696d148d5f8854b4b8a3164bd1e"
REVIEW_SHA = "a3573905cc1008a037d719e9d78ecf36d398aead30dbfbb9d627161910f04b51"
MODEL_LOCK_SHA = "72033cf762140787e445818a7237ba736089f26abff8f7cd04623b5cbc173759"
CANONICAL = Path(r"D:\VR_TMU\XR-Research-V2")
RESOURCE_ROOT = "Assets/VRMaintenanceResearch/ResearchFlow/Resources/"
STREAMING_ROOT = "Assets/StreamingAssets/"


@dataclass(frozen=True)
class Asset:
    path: Path
    sha256: str


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with path.open("rb") as stream:
        result = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def local_path(value, exists=True):
    require(isinstance(value, str) and value.strip(), "Missing absolute local path")
    path = Path(value)
    require(path.is_absolute() and not str(path).startswith(("\\\\", "//")), "Path must be absolute and local: " + value)
    for ancestor in [path] + list(path.parents):
        if ancestor.exists():
            require(not ancestor.is_symlink() and not (getattr(ancestor.lstat(), "st_file_attributes", 0) & 0x400), "Reparse or symlink path refused: " + str(ancestor))
    path = path.resolve()
    if exists:
        require(path.is_file(), "Missing required file: " + str(path))
    return path


def checked_asset(value):
    require(isinstance(value, dict), "Asset requires path and sha256")
    expected = value.get("sha256")
    require(isinstance(expected, str) and len(expected) == 64 and all(c in "0123456789abcdef" for c in expected), "Invalid SHA256")
    path = local_path(value.get("path"))
    require(path.stat().st_size > 0 and digest(path) == expected, "Empty asset or SHA256 mismatch: " + str(path))
    return Asset(path, expected)


def json_asset(asset):
    return json.loads(asset.path.read_text(encoding="utf-8-sig"))


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def png_size(asset):
    with Image.open(asset.path) as image:
        require(image.format == "PNG", "Expected actual PNG: " + str(asset.path))
        width, height = image.size
        image.verify()
    require(width > 0 and height > 0, "PNG has no pixels")
    return width, height


def reviewed_inputs(directory):
    root = local_path(str(directory / "MANIFEST.json")).parent
    require(digest(root / "MANIFEST.json") == REVIEWED_MANIFEST_SHA, "Reviewed document manifest changed")
    require(digest(root.parent / "media-r2-independent-review.md") == REVIEW_SHA, "Independent content review changed")
    require(digest(root / "content.json") == CONTENT_SHA, "Reviewed participant content changed")
    manifest = json.loads((root / "MANIFEST.json").read_text(encoding="utf-8-sig"))
    source = json.loads((root / "content.json").read_text(encoding="utf-8-sig"))
    require(source["schema"] == "research.s1.reference-source.1" and source["approval"] == "DEMO_TEST_NOT_STUDY", "Unsupported content source")
    require(all(source.get(key) is False for key in ["translationsReviewed", "contentApproved", "research_approval_established"]), "Human approvals must remain false")
    sections = {task["task_id"]: task["sections"] for task in source["tasks"]}
    require(set(sections) == {"A", "B"} and all(len(items) == 8 for items in sections.values()), "Expected both S1 tasks and eight sections each")
    require(sum(len(section["paragraphs"]) for items in sections.values() for section in items) == 63, "Expected the reviewed 63 facts")
    expected = {entry["path"]: entry["sha256"] for entry in manifest["files"]}
    documents = {}
    for task, items in sections.items():
        for language in ("th", "ja"):
            prefix = "documents-v3/" + task + "/" + language + "/"
            paths = [prefix + "source.pdf", prefix + "steps.json"] + [prefix + "page-" + str(i + 1) + ".png" for i in range(len(items))]
            assets = [checked_asset({"path": str(root / name), "sha256": expected[name]}) for name in paths]
            require(assets[0].path.read_bytes().startswith(b"%PDF-"), "Invalid source PDF")
            steps = json_asset(assets[1])
            require(steps == [step_for(section) for section in items], "Steps differ from the reviewed content")
            dimensions = [png_size(asset) for asset in assets[2:]]
            documents[(task, language)] = {"pdf": assets[0], "steps": steps, "pages": assets[2:], "dimensions": dimensions}
    checked_asset({"path": str(root / "capture-plan.json"), "sha256": expected["capture-plan.json"]})
    model_lock = Path(__file__).with_name("s1_model_sources.json")
    require(digest(model_lock) == MODEL_LOCK_SHA, "Locked model provenance set changed")
    models = json.loads(model_lock.read_text(encoding="utf-8"))
    return source, sections, documents, {(entry["sha256"], local_path(entry["path"])) for entry in models}


def step_for(section):
    return {"section_id": section["section_id"], "fact_ids": [paragraph["fact_id"] for paragraph in section["paragraphs"]],
            "text_th": section["title"]["th"] + "\n\n" + "\n\n".join(paragraph["th"] for paragraph in section["paragraphs"]),
            "text_ja": section["title"]["ja"] + "\n\n" + "\n\n".join(paragraph["ja"] for paragraph in section["paragraphs"]), "text_en": ""}


def capture_provenance(value, asset, models, task, section=None):
    proof = checked_asset(value)
    record = json_asset(proof)
    require(isinstance(record, dict), "Capture receipt must be an object")
    require(record.get("schema") == "s1.model-media.capture.1" and record.get("origin") == "ACTUAL_MODEL_CAPTURE", "Capture must be an actual model recording, not a synthetic fixture")
    require(record.get("test_data") is True and record.get("engine") in ("UnityEditor", "Blender") and record.get("configuration_kind") in ("NEUTRAL_EXAMPLE", "SIMULATION_EXAMPLE"), "Invalid technical model capture context")
    require(record.get("asset_sha256") == asset.sha256 and record.get("task_id") == task and record.get("section_id") == section, "Capture identity does not match its asset and section")
    utc = datetime.fromisoformat(record.get("captured_utc", "").replace("Z", "+00:00"))
    require(utc.tzinfo is not None and utc.utcoffset().total_seconds() == 0, "Capture timestamp must be UTC")
    require(finite(record.get("time_seconds")) and record["time_seconds"] >= 0 and isinstance(record.get("timebase"), str) and record["timebase"].strip(), "Missing recorded capture timebase")
    require(isinstance(record.get("object_ids"), list) and record["object_ids"] and all(isinstance(value, str) and value.strip() for value in record["object_ids"]), "Missing actual fixture object IDs")
    camera = record.get("camera", {})
    require(isinstance(camera, dict) and isinstance(camera.get("position"), list) and len(camera["position"]) == 3 and all(finite(value) for value in camera["position"])
            and isinstance(camera.get("rotation"), list) and len(camera["rotation"]) == 4 and all(finite(value) for value in camera["rotation"])
            and abs(sum(value * value for value in camera["rotation"]) - 1) <= .01 and finite(camera.get("fov_degrees")) and 0 < camera["fov_degrees"] < 180, "Missing or invalid actual camera pose")
    require(record.get("input_provenance") == "EDUCATIONAL_MODEL_CONFIGURATION_NOT_RUNTIME_INTERACTION", "Model configuration must not claim runtime interaction or E2E")
    project = checked_asset(record.get("source_project"))
    raw = checked_asset(record.get("raw_capture_record"))
    require(asset.sha256 in raw.path.read_text(encoding="utf-8-sig"), "Raw capture record does not contain the asset hash")
    require(isinstance(record.get("source_models"), list) and record["source_models"], "Missing source-model provenance")
    for value in record["source_models"]:
        model = checked_asset(value)
        require((model.sha256, model.path) in models, "Model is not from the locked source or owned imported model set")
    return {"receipt_sha256": proof.sha256, "captured_utc": utc.isoformat(), "time_seconds": record["time_seconds"], "timebase": record["timebase"], "engine": record["engine"],
            "source_project": {"path": str(project.path), "sha256": project.sha256}, "raw_capture_record": {"path": str(raw.path), "sha256": raw.sha256},
            "object_ids": record["object_ids"], "camera": camera, "source_models": record["source_models"], "input_provenance": record["input_provenance"],
            "configuration_kind": record["configuration_kind"]}, record


def participant_media(value, asset, capture, task, section, language, authored, duration=None):
    review_asset = checked_asset(value)
    review = json_asset(review_asset)
    require(isinstance(review, dict) and review.get("schema") == "s1.participant-media.review.1", "Missing participant pixel review")
    require(review.get("scope") == "TECHNICAL_PIXEL_REVIEW_NOT_LANGUAGE_OR_RESEARCH_APPROVAL"
            and review.get("asset_sha256") == asset.sha256 and review.get("capture_receipt_sha256") == checked_asset(capture).sha256
            and review.get("task_id") == task and review.get("section_id") == section and review.get("language") == language
            and review.get("content_sha256") == CONTENT_SHA, "Participant review identity differs from final media")
    require(review.get("visual_review_passed") is True and all(review.get(key) is False for key in
            ("welcome_visible", "controls_visible", "reticle_visible", "test_panel_visible", "developer_overlay_visible"))
            and all(review.get(key) is True for key in ("mechanism_clear", "not_text_wall", "content_retained")), "Participant pixels failed isolation, clarity, or content review")
    texts = review.get("visible_text")
    require(isinstance(texts, list) and all(isinstance(text, str) and text.strip() and text in authored for text in texts), "Visible text is not from reviewed participant content")
    require(not any(re.search(r"\b[AB]\.[a-z]+(?:\.\d+)?\b|NO E2E|test_data|DEV[0-9]+|SCRIPTED MODEL|raw provenance|EDUCATIONAL_MODEL_CONFIGURATION|(?:[A-Za-z]:\\)|sha256", text, re.I) for text in texts), "Developer identifiers or provenance are visible")
    coverage = review.get("coverage")
    require(coverage == {"kind": "WHOLE_IMAGE"} if duration is None else
            isinstance(coverage, dict) and coverage.get("kind") == "FULL_VIDEO" and coverage.get("start_seconds") == 0
            and finite(coverage.get("end_seconds")) and abs(coverage["end_seconds"] - duration) <= .05, "Participant pixel review does not cover the whole asset")
    if duration is not None:
        require(review.get("caption_source_content_sha256") == CONTENT_SHA and review.get("caption_rendering") == "BURNED_IN_REVIEW_REQUIRED", "Final participant caption identity is missing or differs")
    raw = checked_asset(review.get("raw_asset"))
    require(raw.path != asset.path, "Participant asset must have a separate path from immutable raw acquisition")
    kind = review.get("derivation")
    if kind == "CLEAN_COPY":
        require(raw.sha256 == asset.sha256, "Clean participant copy differs from raw bytes")
    elif kind == "PROCESSED_DERIVATIVE":
        processing_asset = checked_asset(review.get("processing_record"))
        processing = json_asset(processing_asset)
        require(raw.sha256 != asset.sha256 and isinstance(processing, dict)
                and processing.get("schema") == "s1.media-processing.1" and processing.get("input_sha256") == raw.sha256
                and processing.get("output_sha256") == asset.sha256 and isinstance(processing.get("operations"), list)
                and processing["operations"] and all(isinstance(item, str) and item.strip() for item in processing["operations"]), "Missing hash-bound derivative processing record")
        if duration is not None:
            require(processing.get("caption_source_content_sha256") == CONTENT_SHA and processing.get("caption_rendering") == "BURNED_IN_REVIEW_REQUIRED", "Derivative caption identity is missing or differs")
    else:
        raise ValueError("Unknown raw-to-participant derivation")
    return raw, {"review_sha256": review_asset.sha256, "participant_asset_sha256": asset.sha256,
                 "raw_asset": {"path": str(raw.path), "sha256": raw.sha256}, "derivation": kind,
                 "processing_record": review.get("processing_record")}


def video_measurement(asset, ffmpeg):
    probe = ffmpeg.with_name("ffprobe" + ffmpeg.suffix)
    require(probe.is_file(), "Missing sibling ffprobe")
    command = [str(probe), "-v", "error", "-protocol_whitelist", "file,crypto,data", "-select_streams", "v:0", "-show_entries", "stream=codec_name,width,height:format=duration", "-of", "json", str(asset.path)]
    measured = subprocess.run(command, capture_output=True, text=True, timeout=120)
    require(measured.returncode == 0 and not measured.stderr.strip(), "Video probe failed: " + measured.stderr)
    data = json.loads(measured.stdout)
    require(len(data.get("streams", [])) == 1, "Video has no primary stream")
    stream = data["streams"][0]
    duration = float(data["format"]["duration"])
    require(stream.get("codec_name") == "h264" and stream.get("width", 0) >= 1280 and stream.get("height", 0) >= 720 and 0 < duration <= 1200, "Expected bounded H.264 model video at least 1280x720")
    command = [str(ffmpeg), "-v", "error", "-protocol_whitelist", "file,crypto,data", "-i", str(asset.path), "-map", "0:v:0", "-an", "-f", "framemd5", "-"]
    decoded = subprocess.run(command, capture_output=True, text=True, timeout=180)
    require(decoded.returncode == 0 and not decoded.stderr.strip(), "Video decode failed: " + decoded.stderr)
    frames = [line for line in decoded.stdout.splitlines() if line and not line.startswith("#")]
    require(len(frames) >= 2 and len({line.rsplit(",", 1)[1].strip() for line in frames}) >= 2, "Video must contain decoded change, not a copied still")
    return {"duration_seconds": duration, "width": stream["width"], "height": stream["height"], "decoded_frames": len(frames), "decoded_change": True}


def caption_quality(value, asset, task, language, chapters, measured, ffmpeg):
    qc_asset = checked_asset(value)
    qc = json_asset(qc_asset)
    require(isinstance(qc, dict), "Caption QC must be an object")
    require(qc.get("schema") == "s1.video-caption-qc.1" and qc.get("video_sha256") == asset.sha256 and qc.get("task_id") == task and qc.get("language") == language
            and qc.get("content_sha256") == CONTENT_SHA and qc.get("scope") == "TECHNICAL_VISUAL_REVIEW_NOT_HUMAN_LANGUAGE_OR_RESEARCH_APPROVAL", "Missing actual chapter caption QC")
    inspections = qc.get("chapters")
    require(isinstance(inspections, list) and len(inspections) == len(chapters), "Caption QC must inspect every chapter")
    for observed, chapter in zip(inspections, chapters):
        require(isinstance(observed, dict), "Caption chapter inspection must be an object")
        require(observed.get("section_id") == chapter["section_id"] and observed.get("captions_sha256") == chapter["captions_sha256"] and observed.get("fact_ids") == chapter["fact_ids"], "Caption QC differs from the reviewed chapter")
        require(observed.get("readable") is True and observed.get("all_facts_visible") is True and type(observed.get("missing_glyphs")) is int and observed["missing_glyphs"] == 0
                and type(observed.get("clipped_glyphs")) is int and observed["clipped_glyphs"] == 0, "Unreadable, missing, or clipped captions")
        require(finite(observed.get("minimum_glyph_height_pixels")) and observed["minimum_glyph_height_pixels"] >= measured["height"] * 24 / 720, "Tiny video captions below the 24px-per-720p engineering floor")
        frames = observed.get("evidence_frames")
        require(isinstance(frames, list) and frames, "Caption QC has no actual movie frames")
        visible_facts = set()
        for frame in frames:
            evidence = checked_asset(frame)
            require(png_size(evidence) == (measured["width"], measured["height"]), "Caption evidence frame dimensions differ from the movie")
            seconds = frame.get("time_seconds")
            require(finite(seconds) and chapter["start_seconds"] <= seconds < chapter["end_seconds"], "Caption evidence time is outside its chapter")
            ids = frame.get("fact_ids_visible")
            require(isinstance(ids, list) and ids and set(ids).issubset(chapter["fact_ids"]), "Caption evidence needs actual visible fact IDs")
            command = [str(ffmpeg), "-v", "error", "-protocol_whitelist", "file,crypto,data", "-i", str(asset.path), "-ss", str(seconds), "-map", "0:v:0", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"]
            decoded = subprocess.run(command, capture_output=True, timeout=120)
            require(decoded.returncode == 0 and not decoded.stderr.strip(), "Caption evidence decode failed: " + decoded.stderr.decode("utf-8", errors="replace"))
            with Image.open(evidence.path) as image:
                require(image.convert("RGB").tobytes() == decoded.stdout, "Caption evidence pixels do not match the movie at its recorded time")
            visible_facts.update(ids)
        require(visible_facts == set(chapter["fact_ids"]), "Caption evidence omits reviewed facts")
    return qc_asset.sha256


def validate_chapters(chapters, sections, language, duration):
    require(isinstance(chapters, list) and len(chapters) == len(sections), "Missing video chapters")
    end = 0
    for chapter, section in zip(chapters, sections):
        require(isinstance(chapter, dict), "Video chapter must be an object")
        require(chapter.get("section_id") == section["section_id"] and chapter.get("fact_ids") == step_for(section)["fact_ids"], "Video chapter does not carry the reviewed facts")
        require(chapter.get("captions_sha256") == hashlib.sha256(step_for(section)["text_" + language].encode("utf-8")).hexdigest(), "Video caption source differs from reviewed content")
        require(finite(chapter.get("start_seconds")) and chapter["start_seconds"] == end and finite(chapter.get("end_seconds")) and chapter["end_seconds"] > end, "Video chapters must be ordered, contiguous, and nonempty")
        end = chapter["end_seconds"]
    require(abs(end - duration) <= .05, "Chapters must cover the measured movie")
    return chapters


def capture_inputs(manifest_path, sections, documents, models, ffmpeg):
    captures_path = local_path(str(manifest_path))
    captures = json.loads(captures_path.read_text(encoding="utf-8-sig"))
    require(isinstance(captures, dict), "Capture manifest must be an object")
    require(captures.get("schema") == "s1.runtime-media.inputs.1" and captures.get("content_sha256") == CONTENT_SHA, "Capture manifest does not match reviewed content")
    expected_sections = {section["section_id"] for items in sections.values() for section in items}
    images = captures.get("images", [])
    videos = captures.get("videos", [])
    require(isinstance(images, list) and len(images) == 16 and all(isinstance(image, dict) for image in images) and {image.get("section_id") for image in images} == expected_sections, "Required actual model Images are missing or repeated. Expected all 16 authored sections.")
    require(isinstance(videos, list) and len(videos) == 4 and all(isinstance(video, dict) for video in videos) and {(video.get("task_id"), video.get("language")) for video in videos} == set(documents), "Required actual model Video is missing or repeated. Expected A/th, A/ja, B/th, and B/ja.")
    document_hashes = {asset.sha256 for document in documents.values() for asset in document["pages"]}
    image_map, video_map = {}, {}
    for image in images:
        section = image["section_id"]
        asset = checked_asset(image.get("asset"))
        require(asset.sha256 not in document_hashes, "A PDF raster is not a model Image")
        width, height = png_size(asset)
        require(width >= 640 and height >= 360, "Model Image is below the 640x360 engineering floor")
        authored = next(step_for(item) for item in sections[section[0]] if item["section_id"] == section)
        raw, review = participant_media(image.get("participant_review"), asset, image.get("capture"), section[0], section, "neutral", authored["text_th"] + "\n" + authored["text_ja"])
        provenance, _ = capture_provenance(image.get("capture"), raw, models, section[0], section)
        image_map[section] = {"asset": asset, "provenance": {**provenance, "participant_review": review}}
    for video in videos:
        task, language = video["task_id"], video["language"]
        asset = checked_asset(video.get("asset"))
        require(asset.path.suffix.lower() == ".mp4", "Expected actual MP4")
        measured = video_measurement(asset, ffmpeg)
        authored = "\n".join(step_for(item)["text_" + language] for item in sections[task])
        raw, review = participant_media(video.get("participant_review"), asset, video.get("capture"), task, None, language, authored, measured["duration_seconds"])
        provenance, record = capture_provenance(video.get("capture"), raw, models, task)
        provenance["participant_review"] = review
        require(record.get("caption_source_content_sha256") == CONTENT_SHA and record.get("caption_rendering") in ("MODEL_ONLY_RAW", "BURNED_IN_REVIEW_REQUIRED"), "Raw capture caption metadata must identify model-only or historical burned-in acquisition")
        require(record.get("caption_rendering") != "MODEL_ONLY_RAW" or review["derivation"] == "PROCESSED_DERIVATIVE", "Model-only raw video requires a processed participant derivative, not CLEAN_COPY")
        chapters = validate_chapters(video.get("chapters"), sections[task], language, measured["duration_seconds"])
        qc_sha = caption_quality(video.get("caption_qc"), asset, task, language, chapters, measured, ffmpeg)
        video_map[(task, language)] = {"asset": asset, "provenance": provenance, "measurement": measured, "chapters": chapters, "caption_qc_sha256": qc_sha}
    return image_map, video_map, digest(captures_path)


def package_files(source, sections, documents, images, videos, origin):
    files, packages = {}, []
    for (task, language), document in documents.items():
        prefix = "ResearchReaderDemo/S1/" + task + "/" + language + "/"
        package = {"schema_version": 1, "content_id": "s1." + task + "." + language, "version": source["version"],
                   "task_id": task, "language": language, "source_content_sha256": CONTENT_SHA, "media_origin": origin,
                   "source_sha256": document["pdf"].sha256, "source_pdf": "S1_" + task + "_" + language + ".pdf", "source_pdf_resource": prefix + "source_pdf",
                   "approval": source["approval"], "translationsReviewed": False, "contentApproved": False, "research_approval_established": False,
                   "pages": [], "steps": document["steps"], "images": []}
        files[RESOURCE_ROOT + prefix + "source_pdf.bytes"] = document["pdf"]
        for i, section in enumerate(sections[task]):
            identity = {"section_id": section["section_id"], "fact_ids": step_for(section)["fact_ids"]}
            page = document["pages"][i]
            page_resource = prefix + "pages/p" + str(i + 1).zfill(2)
            width, height = document["dimensions"][i]
            package["pages"].append({**identity, "page": i + 1, "resource": page_resource, "sha256": page.sha256, "width": width, "height": height})
            files[RESOURCE_ROOT + page_resource + ".bytes"] = page
            image = images[section["section_id"]]
            image_resource = prefix + "images/i" + str(i + 1).zfill(2)
            text = step_for(section)
            package["images"].append({**identity, "resource": image_resource, "sha256": image["asset"].sha256,
                                      "caption_th": text["text_th"], "caption_ja": text["text_ja"], "caption_en": ""})
            files[RESOURCE_ROOT + image_resource + ".bytes"] = image["asset"]
        video = videos[(task, language)]
        movie = prefix + "model.mp4"
        chapters = [{"section_id": chapter["section_id"], "fact_ids": chapter["fact_ids"], "start_seconds": chapter["start_seconds"], "end_seconds": chapter["end_seconds"],
                     "title_th": section["title"]["th"], "title_ja": section["title"]["ja"]} for chapter, section in zip(video["chapters"], sections[task])]
        package["video"] = {"streaming_path": movie, "sha256": video["asset"].sha256, "duration_seconds": video["measurement"]["duration_seconds"], "chapters": chapters}
        files[STREAMING_ROOT + movie] = video["asset"]
        files[RESOURCE_ROOT + prefix + "package.json"] = (json.dumps(package, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        packages.append({"resource": prefix + "package", "content_id": package["content_id"], "task_id": task, "language": language, "media_origin": origin,
                         "acquisition_receipts": {"images": [{"section_id": section["section_id"], "provenance": images[section["section_id"]].get("provenance")} for section in sections[task]],
                                                  "video": video.get("provenance"), "video_measurement": video["measurement"], "caption_qc_sha256": video.get("caption_qc_sha256")}})
    return files, packages


def write_package(output, files, packages, capture_sha):
    output = local_path(str(output), exists=False)
    require(not output.exists() and not output.is_relative_to(CANONICAL.resolve()), "Output must be fresh external space")
    require(output.parent.is_dir(), "Output parent must already exist")
    staging = output.parent / ("s1-stage-" + uuid.uuid4().hex[:12])
    require(os.name != "nt" or all(max(len(str(output / relative)), len(str(staging / relative))) + 5 < 260 for relative in files), "Output path exceeds the supported Windows path length")
    staging.mkdir()
    records = []
    try:
        for relative, value in files.items():
            path = staging / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            if isinstance(value, Asset):
                require(digest(value.path) == value.sha256, "Input changed before copy: " + str(value.path))
                shutil.copyfile(value.path, path)
                require(digest(path) == value.sha256, "Copy hash mismatch")
            else:
                path.write_bytes(value)
            records.append({"path": relative, "sha256": digest(path), "bytes": path.stat().st_size})
            if path.suffix != ".mp4":
                guid = hashlib.sha256(("s1.runtime-media/" + relative).encode()).hexdigest()[:32]
                meta = Path(str(path) + ".meta")
                meta.write_text("fileFormatVersion: 2\nguid: " + guid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
                records.append({"path": relative + ".meta", "sha256": digest(meta), "bytes": meta.stat().st_size})
        actual = all(package["media_origin"] == "ACTUAL_MODEL_CAPTURE" for package in packages)
        result = {"schema": "s1.runtime-media.package-result.1", "status": "TECHNICAL_DRAFT_HASH_AND_DECODE_VERIFIED_UNITY_REVIEW_PENDING" if actual else "SYNTHETIC_UNIT_FIXTURE_NOT_S1_MEDIA",
                  "content_sha256": CONTENT_SHA, "capture_manifest_sha256": capture_sha, "all_four_formats_nonempty": True, "media_accepted": False,
                  "translationsReviewed": False, "contentApproved": False, "research_approval_established": False, "unity_trial_verified": False,
                  "packages": packages, "files": records}
        (staging / "packaging-result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        require(not output.exists(), "Output collision before commit")
        os.rename(staging, output)
        return result
    except BaseException:
        require(staging.parent == output.parent and staging.name.startswith("s1-stage-"), "Owned staging containment failed")
        shutil.rmtree(staging)
        raise


def main():
    parser = argparse.ArgumentParser(description="Package reviewed S1 references only when all real model media is present.")
    parser.add_argument("--reviewed-dir", type=Path, required=True)
    parser.add_argument("--captures", type=Path, required=True)
    parser.add_argument("--ffmpeg", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        ffmpeg = local_path(str(args.ffmpeg))
        output = local_path(str(args.output), exists=False)
        require(not output.exists() and not output.is_relative_to(CANONICAL.resolve()), "Output must be fresh external space")
        source, sections, documents, models = reviewed_inputs(args.reviewed_dir)
        images, videos, capture_sha = capture_inputs(args.captures, sections, documents, models, ffmpeg)
        files, packages = package_files(source, sections, documents, images, videos, "ACTUAL_MODEL_CAPTURE")
        result = write_package(output, files, packages, capture_sha)
        print(json.dumps({"output": str(output), "status": result["status"], "packages": len(packages)}, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError, Image.DecompressionBombError) as error:
        print("S1_MEDIA_INPUT_ERROR: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
