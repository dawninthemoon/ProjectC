"""Validate exported JSON and publish identical runtime card/deck catalogs; never reads XLSX."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from CardDataValidation import validate_effects, expanded_description_data

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "ProjectC.Client/Assets/Resources"
SERVER = ROOT / "ProjectC.Server/Data/HyakkiYagyo"
CARD_SOURCE = RESOURCES / "ProjectC_Data - CardData.json"
LOCALIZE_SOURCE = RESOURCES / "ProjectC_Data - Localize.json"
DECK_SOURCE = RESOURCES / "ProjectC_Data - DeckData.json"
DESTINATIONS = [SERVER / "Cards.json", RESOURCES / "CardData/HyakkiCards.json"]
DECK_DESTINATIONS = [SERVER / "Decks.json", RESOURCES / "CardData/HyakkiDecks.json"]
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


def integer_at(value, minimum, maximum, context):
    try:
        return integer(value, minimum, maximum)
    except ValueError as error:
        raise ValueError(f"{context}: {error}") from error


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
    used = set()
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
        else:
            used.add(int(token))
        offset = end + 1
    if used != set(range(argument_count)):
        raise ValueError(f"DescriptionArgs/template index mismatch: used {sorted(used)}, arguments {argument_count}")


def generate(rows=None, translations=None):
    translations = load(LOCALIZE_SOURCE) if translations is None else translations
    if not isinstance(translations, list):
        raise ValueError("Localize export must be an array")
    localize = {}
    for row in translations:
        if not isinstance(row, dict):
            raise ValueError(f"{LOCALIZE_SOURCE}: Localize row must be an object")
        key = row.get("lmk")
        if not isinstance(key, str) or not key.strip() or key in localize:
            raise ValueError(f"Missing/duplicate localization key: {key}")
        for language, value in row.items():
            if language != "lmk" and (language not in LANGUAGES or (value is not None and not isinstance(value, str))):
                raise ValueError(f"Invalid translation: {key}/{language}")
        if not any(row.get(language, "") for language in ("korean", "english")):
            raise ValueError(f"No fallback translation: {key}")
        localize[key] = row

    rows = load(CARD_SOURCE) if rows is None else rows
    if not isinstance(rows, list) or not rows:
        raise ValueError("Card export must be a nonempty array")
    metadata = load(SERVER / "CardMetadata.json")
    versions, ids, cards = set(), set(), []
    for index, source in enumerate(rows):
        if not isinstance(source, dict):
            raise ValueError(f"{CARD_SOURCE}: row {index + 1}: expected object")
        card = dict(source)
        card["Id"] = integer_at(card.get("Id"), 1, 1000000, f"{CARD_SOURCE}: row {index + 1}.Id")
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
                card[field] = integer_at(card.get(field), 1 if field == "MaxHp" and card["Type"] == "Monster" else 0,
                                         100000, f"{CARD_SOURCE}: Card {card['Id']}.{field}")
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
                raise ValueError(f"{CARD_SOURCE}: Card {card['Id']}.{field}: missing key {card.get(field)!r} in {LOCALIZE_SOURCE}")
        validate_effects(card)
        expanded = expanded_description_data(card)
        for argument_index, argument in enumerate(card["DescriptionArgs"]):
            # A fallback must not conceal a typo or a nonexistent effect/action index.
            try:
                if not isinstance(argument, dict):
                    raise ValueError("expected argument object")
                resolve(expanded, {"path": argument.get("path")})
                resolve(card, argument)
            except ValueError as error:
                raise ValueError(f"{CARD_SOURCE}: Card {card['Id']}.DescriptionArgs[{argument_index}]: {error}") from error
        for language in LANGUAGES:
            text = localize[card["DescriptionKey"]].get(language)
            if text:
                try:
                    validate_template(text, len(card["DescriptionArgs"]), localize)
                except ValueError as error:
                    raise ValueError(f"{LOCALIZE_SOURCE}: {card['DescriptionKey']}/{language}: {error}") from error
        cards.append(card)
    if len(versions) != 1 or not all(isinstance(version, str) and version.strip() for version in versions):
        raise ValueError("One nonempty CatalogVersion is required")
    if set(metadata) - {str(card_id) for card_id in ids}:
        raise ValueError("Compatibility metadata references an unknown card")
    for card in cards:
        if any(type(target) is not int or target not in ids for target in card["AllowedManifestations"]):
            raise ValueError(f"Card {card['Id']}.AllowedManifestations: unknown definition")
        for effect in card["Effects"]:
            for action in effect["Actions"]:
                if action["Operation"] == "Transform" and action["TransformDefinitionId"] not in ids:
                    raise ValueError(f"Card {card['Id']}: unknown TransformDefinitionId {action['TransformDefinitionId']}")
    cards.sort(key=lambda card: card["Id"])
    canonical = json.dumps(cards, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    version = next(iter(versions)) + "-" + hashlib.sha256(canonical).hexdigest()[:12]
    if len(version) > 64:
        raise ValueError("CatalogVersion exceeds server protocol limit")
    return json.dumps({"CatalogVersion": version, "Cards": cards}, ensure_ascii=False, indent=2) + "\n"


def generate_decks(catalog, rows=None, existing=None):
    rows = load(DECK_SOURCE) if rows is None else rows
    existing = load(SERVER / "Decks.json") if existing is None else existing
    if not isinstance(rows, list) or not rows:
        raise ValueError(f"{DECK_SOURCE}: expected nonempty row array")
    definitions = {card["Id"]: card for card in catalog["Cards"]}
    config = existing["Config"]
    grouped, pairs = {}, set()
    for index, row in enumerate(rows):
        at = f"{DECK_SOURCE}: row {index + 1}"
        if not isinstance(row, dict):
            raise ValueError(f"{at}: expected object")
        deck_id, name = row.get("DeckId"), row.get("\uB371 \uC774\uB984")
        if not isinstance(deck_id, str) or not deck_id.strip() or len(deck_id) > 64 or not isinstance(name, str) or not name.strip():
            raise ValueError(f"{at}: DeckId and deck name are required")
        card_id = integer_at(row.get("CardId"), 1, 1000000, at + ".CardId")
        count = integer_at(row.get("\uB9E4\uC218"), 1, config["MaxCardCopies"], at + ".count")
        if (deck_id, card_id) in pairs:
            raise ValueError(f"{at}: duplicate card {card_id} in deck {deck_id}")
        pairs.add((deck_id, card_id))
        definition = definitions.get(card_id)
        if definition is None or not definition.get("MainDeckEligible", True):
            raise ValueError(f"{at}: {deck_id} references unknown/ineligible card {card_id}")
        deck = grouped.setdefault(deck_id, {"Id": deck_id, "Name": name, "Cards": []})
        if deck["Name"] != name:
            raise ValueError(f"{at}: conflicting names for deck {deck_id}")
        if row.get("\uD569\uACC4") not in (None, ""):
            total = integer_at(row["\uD569\uACC4"], config["MinDeckSize"], config["MaxDeckSize"], at + ".total")
            if total != config["MinDeckSize"]:
                raise ValueError(f"{at}: incorrect declared deck total")
        deck["Cards"].extend([card_id] * count)
    for deck in grouped.values():
        if not config["MinDeckSize"] <= len(deck["Cards"]) <= config["MaxDeckSize"]:
            raise ValueError(f"{DECK_SOURCE}: deck {deck['Id']} has {len(deck['Cards'])} cards; expected {config['MinDeckSize']}..{config['MaxDeckSize']}")
    # DeckData owns only its listed IDs. Preserve the runtime config and all other decks.
    decks = []
    for deck in existing["Decks"]:
        decks.append(grouped.pop(deck["Id"], deck))
    decks.extend(grouped.values())
    return json.dumps({"Config": config, "Decks": decks}, ensure_ascii=False, indent=2) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail if generated server/Unity artifacts are stale")
    args = parser.parse_args()
    try:
        result = generate()
        decks = generate_decks(json.loads(result))
    except (ValueError, KeyError, TypeError) as error:
        parser.exit(2, f"JSON publication failed ({CARD_SOURCE}, {DECK_SOURCE}): {error}\n")
    outputs = [(path, result) for path in DESTINATIONS] + [(path, decks) for path in DECK_DESTINATIONS]
    # Validate all documents before changing any artifact.
    for path, content in outputs:
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                raise ValueError(f"Stale generated catalog: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8")
    print(("Verified" if args.check else "Published") + " server/Unity cards and decks: " + json.loads(result)["CatalogVersion"])


if __name__ == "__main__":
    main()
