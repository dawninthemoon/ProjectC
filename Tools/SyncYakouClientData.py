#!/usr/bin/env python3
"""Copy authored Yakou presentation data into Unity without changing the source."""
from __future__ import annotations

import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "ProjectC.Server" / "Data" / "YakouPrototype"
TARGET = ROOT / "ProjectC.Client" / "Assets" / "Resources" / "CardData"
FILES = {
    "Cards.json": "YakouPrototypeCards.json",
    "Decks.json": "YakouPrototypeDecks.json",
    "Localize.json": "YakouPrototypeLocalize.json",
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="verify the Unity copies (default)")
    mode.add_argument("--write", action="store_true", help="update the Unity copies")
    args = parser.parse_args()
    mismatches = []
    for source_name, target_name in FILES.items():
        source = SOURCE / source_name
        target = TARGET / target_name
        content = source.read_bytes()
        if args.write:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
        if not target.is_file() or target.read_bytes() != content:
            mismatches.append(target_name)
    if mismatches:
        print("Yakou Unity resource copies differ: " + ", ".join(mismatches))
        return 1
    print("Yakou Unity card, deck and localization resources match the server source.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
