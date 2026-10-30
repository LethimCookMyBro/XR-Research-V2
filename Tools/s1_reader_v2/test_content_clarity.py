import argparse
import json
import sys
from pathlib import Path


LANGUAGES = ("th", "ja", "en")
FORMATS = ("manual", "steps", "illustrated", "video")
FRONT_VIEW = {"th": "ด้านหน้า", "ja": "正面", "en": "front"}
CHOICE = {"th": "เลือก", "ja": "選", "en": "choose"}


def nodes(value):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from nodes(child)
    elif isinstance(value, list):
        for child in value:
            yield from nodes(child)


def localized_text(value, language):
    if isinstance(value, str):
        return value
    if isinstance(value, list):
        return " ".join(localized_text(child, language) for child in value)
    if isinstance(value, dict):
        if language in value:
            return localized_text(value[language], language)
        return " ".join(localized_text(child, language) for key, child in value.items()
                        if key not in ("units", "id", "shot", "anchor"))
    return ""


def check(root):
    directory = root / "Tools" / "s1_reader_v2"
    content = {task: json.loads((directory / f"content_{task}.json").read_text(encoding="utf-8-sig"))
               for task in "AB"}
    failures = []
    count = 0

    def require(condition, label):
        nonlocal count
        count += 1
        if not condition:
            failures.append(label)

    for task, document in content.items():
        for index, node in enumerate(nodes(document)):
            if any(language in node for language in LANGUAGES):
                require(all(language in node for language in LANGUAGES),
                        f"{task} localized node {index} contains TH/JA/EN")
                present = [node.get(language) for language in LANGUAGES]
                require(len({type(value) for value in present}) == 1,
                        f"{task} localized node {index} has equivalent value shape")
                if isinstance(present[0], list):
                    require(len({len(value) for value in present if isinstance(value, list)}) == 1,
                            f"{task} localized node {index} has equivalent narration segments")

    for format_name in FORMATS:
        requirement = [node for node in nodes(content["A"][format_name])
                       if "ssd_req" in node.get("units", [])]
        for language in LANGUAGES:
            text = localized_text(requirement, language)
            require(all(token in text for token in ("SATA", "2.5", "500", "GB", "600", "TBW")),
                    f"A/{format_name}/{language} SSD says SATA 2.5-inch, 500 GB and 600 TBW")

    note = next(item for item in content["A"]["manual"] if item["id"] == "notes")["items"][6]["th"]
    require("เครื่องติด" not in note and "ใส่" in note,
            "A manual Thai distinguishes physical fit from powered boot")

    choices = {
        "core": content["B"]["core"]["do"],
        "illustrated": content["B"]["illustrated"][0]["card"]["lines"][0],
        "video": content["B"]["video"][0]["say"],
    }
    for format_name, value in choices.items():
        for language in LANGUAGES:
            text = localized_text(value, language).lower()
            require(CHOICE[language] in text,
                    f"B/{format_name}/{language} leaves inspection-area choice to participant")

    for format_name, indices in (("steps", (4, 7, 9)), ("video", (5, 7, 9, 10))):
        for index in indices:
            for language in LANGUAGES:
                text = localized_text(content["B"][format_name][index], language).lower()
                require(FRONT_VIEW[language] in text,
                        f"B/{format_name}/{index}/{language} defines front-view turning direction")

    for index in (4, 9):
        for language in LANGUAGES:
            require(content["B"]["steps"][index]["look"][language].startswith("⚠"),
                    f"B/steps/{index}/{language} keeps the reverse-thread warning style")

    require("นั่งเต็ม" not in localized_text(content["B"], "th"),
            "B Thai uses natural wording for fully seated parts")
    for failure in failures:
        print("FAIL " + failure)
    print(f"content clarity: {'FAIL' if failures else 'PASS'} ({count - len(failures)}/{count} checks)")
    return 1 if failures else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    sys.exit(check(parser.parse_args().root))
