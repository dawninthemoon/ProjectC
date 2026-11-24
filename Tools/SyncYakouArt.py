#!/usr/bin/env python3
"""Copy existing presentation art into Yakou's isolated Resources namespace."""

import argparse
import csv
import json
from pathlib import Path
import re
import shutil
import uuid


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ProjectC.Client/Assets/Resources/CardSprite"
DESTINATION = ROOT / "ProjectC.Client/Assets/Resources/YakouCardSprite"
CATALOG = ROOT / "ProjectC.Server/Data/YakouPrototype/Cards.json"
LOCALIZE = ROOT / "ProjectC.Server/Data/YakouPrototype/Localize.json"
MAPPING_CSV = ROOT / "ProjectC.Client/Docs/YakouCardArtMapping.csv"

# Source IDs refer only to authored GameCore artwork files. No rules or catalog
# IDs are shared at runtime: Yakou always loads from its own Resources folder.
ART = {
    2001: 1001, 2002: 2004, 2003: 1007, 2004: 1006,
    2005: 1103, 2006: 1005, 2007: 1004, 2008: 1101,
    2009: 3002, 2010: 4005, 2011: 4001, 2012: 4003,
    2101: 1003, 2102: 1102,
    3001: 1002, 3002: 3006, 3003: 2005, 3004: 3004,
    3005: 3001, 3006: 4006, 3007: 1008, 3008: 4002,
    3009: 1104, 3010: 3003,
    3011: 2001, 3012: 2002, 3013: 3005, 3014: 2003,
    3015: 4004, 3016: 4004, 3017: 4001, 3018: 2003,
    3019: 3002, 3020: 2001,
    4001: 3004, 4002: 1002, 4003: 3006, 4004: 1102,
    4005: 1004, 4006: 2002, 4007: 1007, 4008: 3003,
    4009: 1006, 4010: 1008, 4011: 1101, 4012: 4005,
    4013: 2001, 4014: 1005, 4015: 3005, 4016: 4002,
    4017: 4003, 4018: 4004, 4019: 1003, 4020: 2005,
    4021: 1103,
}


def metadata(source: Path, target: Path) -> str:
    text = source.with_name(source.name + ".meta").read_text(encoding="utf-8")
    text = re.sub(r"(?m)^guid: [0-9a-f]{32}$", "guid: " + uuid.uuid4().hex, text, count=1)
    return "\n".join(line.rstrip() for line in text.replace(source.stem, target.stem).splitlines()) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="create or update copied images and the mapping CSV")
    args = parser.parse_args()
    cards = json.loads(CATALOG.read_text(encoding="utf-8"))["Cards"]
    definitions = {card["Id"]: card for card in cards}
    if set(definitions) != set(ART):
        raise ValueError("Art mapping must cover every Yakou card definition exactly once")
    translations = {row["lmk"]: row["korean"] for row in json.loads(LOCALIZE.read_text(encoding="utf-8"))}
    rows = []
    problems = []
    if args.write:
        DESTINATION.mkdir(parents=True, exist_ok=True)
        folder_meta = DESTINATION.with_name(DESTINATION.name + ".meta")
        if not folder_meta.exists():
            folder_meta.write_text(
                "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex
                + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n"
                + "  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    for definition_id, source_id in sorted(ART.items()):
        source = SOURCE / f"CardSprite_{source_id}.jpg"
        target = DESTINATION / f"CardSprite_{definition_id}.jpg"
        target_meta = target.with_name(target.name + ".meta")
        if not source.is_file():
            problems.append(f"missing source {source.name}")
            continue
        if args.write:
            shutil.copyfile(source, target)
            if not target_meta.exists():
                target_meta.write_text(metadata(source, target), encoding="utf-8")
        if not target.is_file() or target.read_bytes() != source.read_bytes():
            problems.append(f"image mismatch {definition_id}")
        if not target_meta.is_file():
            problems.append(f"missing sprite metadata {definition_id}")
        else:
            meta = target_meta.read_text(encoding="utf-8")
            if "spriteMode: 2" not in meta or "textureType: 8" not in meta or target.stem + "_0" not in meta:
                problems.append(f"invalid sprite metadata {definition_id}")
        rows.append((definition_id, translations[definitions[definition_id]["NameKey"]],
                     target.name, source.name))
    if args.write:
        with MAPPING_CSV.open("w", encoding="utf-8", newline="") as output:
            writer = csv.writer(output, lineterminator="\n")
            writer.writerow(("DefinitionId", "KoreanName", "YakouSprite", "SourceSprite"))
            writer.writerows(rows)
    if not MAPPING_CSV.is_file():
        problems.append("missing mapping CSV")
    else:
        with MAPPING_CSV.open(encoding="utf-8", newline="") as source:
            listed = list(csv.reader(source))
        expected = [["DefinitionId", "KoreanName", "YakouSprite", "SourceSprite"]]
        expected.extend([str(row[0]), *row[1:]] for row in rows)
        if listed != expected:
            problems.append("mapping CSV does not match the catalog")
    metadata_files = sorted(DESTINATION.glob("*.jpg.meta"))
    metadata_files.append(DESTINATION.with_name(DESTINATION.name + ".meta"))
    guids = []
    for path in metadata_files:
        if not path.is_file():
            problems.append(f"missing metadata {path.name}")
            continue
        match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"))
        if match is None:
            problems.append(f"invalid GUID {path.name}")
        else:
            guids.append(match.group(1))
    if len(guids) != len(set(guids)):
        problems.append("duplicate Yakou asset GUID")
    if problems:
        raise ValueError("; ".join(problems))
    print(f"Verified {len(rows)} Yakou sprites from {len(set(ART.values()))} existing images")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
