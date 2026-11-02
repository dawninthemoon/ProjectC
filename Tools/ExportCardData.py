"""Validate Sheets exports and publish one identical Hyakki catalog to server and Unity."""
import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "ProjectC.Client/Assets/Resources"
SERVER = ROOT / "ProjectC.Server/Data/HyakkiYagyo"
CARD_SOURCE = RESOURCES / "ProjectC_Data - CardData.json"
LOCALIZE_SOURCE = RESOURCES / "ProjectC_Data - Localize.json"
DESTINATIONS = [SERVER / "Cards.json", RESOURCES / "CardData/HyakkiCards.json"]
LANGUAGES = {"korean", "english", "japanese", "zh-tw", "zh-cn", "portuguese"}
PATH = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(?:\[[0-9]+\]|\.[A-Za-z_][A-Za-z0-9_]*)*$")
PART = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)|\[([0-9]+)\]")
KEYWORD = re.compile(r"^([^{}]+):@([A-Za-z][A-Za-z0-9_]*)$")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def parse(text):
    return json.loads(text, object_pairs_hook=unique_object)


def load(path):
    return parse(path.read_text(encoding="utf-8-sig"))


def integer(value, minimum, maximum):
    if isinstance(value, str) and re.fullmatch(r"-?[0-9]+", value):
        value = int(value)
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"Invalid integer: {value!r}")
    return value


def resolve(card, argument):
    path = argument.get("path")
    if not isinstance(path, str) or len(path) > 256 or not PATH.fullmatch(path):
        raise ValueError(f"Invalid read-only path: {path}")
    if set(argument) - {"path", "default"}:
        raise ValueError(f"Unexpected argument fields: {argument}")
    value = card
    for part in PART.finditer(path):
        key, index = part.groups()
        if key is not None:
            value = value.get(key) if isinstance(value, dict) else None
        else:
            value = value[int(index)] if isinstance(value, list) and int(index) < len(value) else None
        if value is None:
            break
    if value is None:
        value = argument.get("default")
    if value is None or isinstance(value, (dict, list)):
        raise ValueError(f"Missing/scalar description path: {path}")
    return value


def validate_template(text, argument_count, localize):
    offset = 0
    while offset < len(text):
        char = text[offset]
        if char in "{}" and offset + 1 < len(text) and text[offset + 1] == char:
            offset += 2
            continue
        if char == "}":
            raise ValueError(f"Unmatched brace: {text}")
        if char != "{":
            offset += 1
            continue
        end = text.find("}", offset + 1)
        if end < 0:
            raise ValueError(f"Unclosed token: {text}")
        token = text[offset + 1:end]
        keyword = KEYWORD.fullmatch(token)
        if keyword:
            for suffix in ("name", "desc"):
                key = f"keyword_{keyword[2]}_{suffix}"
                if key not in localize:
                    raise ValueError(f"Missing keyword definition: {key}")
        elif not token.isascii() or not token.isdigit() or int(token) >= argument_count:
            raise ValueError(f"Invalid argument index/token: {token}")
        offset = end + 1


def generate():
    translations = load(LOCALIZE_SOURCE)
    if not isinstance(translations, list):
        raise ValueError("Localize export must be an array")
    localize = {}
    for row in translations:
        key = row.get("lmk")
        if not isinstance(key, str) or not key.strip() or key in localize:
            raise ValueError(f"Missing/duplicate localization key: {key}")
        for language, value in row.items():
            if language != "lmk" and (language not in LANGUAGES or (value is not None and not isinstance(value, str))):
                raise ValueError(f"Invalid translation: {key}/{language}")
        if not any(row.get(language, "") for language in ("korean", "english")):
            raise ValueError(f"No fallback translation: {key}")
        localize[key] = row

    rows = load(CARD_SOURCE)
    if not isinstance(rows, list) or not rows:
        raise ValueError("Card export must be a nonempty array")
    metadata = load(SERVER / "CardMetadata.json")
    versions, ids, cards = set(), set(), []
    for source in rows:
        card = dict(source)
        card["Id"] = integer(card["Id"], 1, 1000000)
        if card["Id"] in ids:
            raise ValueError(f"Duplicate card: {card['Id']}")
        ids.add(card["Id"])
        versions.add(card.pop("CatalogVersion"))
        if card["Type"] not in ("Monster", "Spell"):
            raise ValueError(f"Invalid card type: {card['Type']}")
        for field in ("Archetypes", "AllowedManifestations", "Effects", "DescriptionArgs"):
            value = card.get(field, [])
            if isinstance(value, str):
                value = parse(value) if value.strip() else []
            if not isinstance(value, list):
                raise ValueError(f"Expected array: {field}")
            card[field] = value
        for field in ("Attack", "MaxHp"):
            if card.get(field) == "":
                card.pop(field)
            if field in card or card["Type"] == "Monster":
                card[field] = integer(card.get(field), 1 if field == "MaxHp" and card["Type"] == "Monster" else 0, 100000)
        # Sheets currently omits the existing manifest-only rule. Keep that
        # explicit compatibility metadata until the column is added to Sheets.
        for key, value in metadata.get(str(card["Id"]), {}).items():
            if key not in {"MainDeckEligible", "Traits", "ArtKey"}:
                raise ValueError(f"Invalid compatibility metadata field: {key}")
            card.setdefault(key, value)
        if "MainDeckEligible" in card and type(card["MainDeckEligible"]) is not bool:
            raise ValueError("MainDeckEligible must be a boolean")
        for field in ("NameKey", "DescriptionKey"):
            if not isinstance(card.get(field), str) or card[field] not in localize:
                raise ValueError(f"Missing localization key: {card.get(field)}")
        for argument in card["DescriptionArgs"]:
            resolve(card, argument)
        for language in LANGUAGES:
            text = localize[card["DescriptionKey"]].get(language)
            if text:
                validate_template(text, len(card["DescriptionArgs"]), localize)
        cards.append(card)
    if len(versions) != 1 or not all(isinstance(version, str) and version.strip() for version in versions):
        raise ValueError("One nonempty CatalogVersion is required")
    if set(metadata) - {str(card_id) for card_id in ids}:
        raise ValueError("Compatibility metadata references an unknown card")
    for card in cards:
        if any(type(target) is not int or target not in ids for target in card["AllowedManifestations"]):
            raise ValueError("Unknown manifestation definition")
    cards.sort(key=lambda card: card["Id"])
    canonical = json.dumps(cards, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    version = next(iter(versions)) + "-" + hashlib.sha256(canonical).hexdigest()[:12]
    if len(version) > 64:
        raise ValueError("CatalogVersion exceeds server protocol limit")
    return json.dumps({"CatalogVersion": version, "Cards": cards}, ensure_ascii=False, indent=2) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail if generated server/Unity artifacts are stale")
    args = parser.parse_args()
    result = generate()
    for path in DESTINATIONS:
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != result:
                raise ValueError(f"Stale generated catalog: {path.relative_to(ROOT)}")
        else:
            path.write_text(result, encoding="utf-8")
    print(("Verified" if args.check else "Published") + " identical server/Unity catalog: " + json.loads(result)["CatalogVersion"])


if __name__ == "__main__":
    main()
