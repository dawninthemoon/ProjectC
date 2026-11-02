"""Fail-fast validation for the existing exported card JSON dialect."""
from copy import deepcopy

OPERATIONS = {
    "Draw", "Mill", "Salvage", "Search", "SummonFromDeck", "SummonFromGraveyard",
    "ReturnToHand", "Destroy", "DealDamage", "Heal", "ModifyStats", "Transform",
    "Discard", "SendToGraveyard", "SummonFromHand", "Exile", "GainYokaiPower",
}
TRIGGERS = {"None", "OnSummoned", "OnDestroyed", "OnDamaged", "OnTurnStarted",
            "OnTurnEnded", "OnNormalSummoned", "OnRevived", "OnManifested"}
CATEGORIES = {"Self", "FriendlyMonster", "EnemyMonster", "AnyMonster", "FriendlyPlayer",
              "EnemyPlayer", "FriendlyDeck", "FriendlyHand", "FriendlyGraveyard", "EnemyGraveyard"}
ZONES = {"Deck", "Hand", "Field", "Graveyard", "Exile"}
COSTS = {"YokaiPower", "Life", "Discard", "Tribute", "HiddenExileTop"}
TARGET_DEFAULTS = dict(Zones=["Field"], CardType="Monster", MinCount=1, MaxCount=1,
                       RequiredTrait="", RequiredArchetype="", MaxAttack=-1)
ACTION_DEFAULTS = dict(Amount=1, AttackDelta=0, MaxHpDelta=0, AllTargets=False,
                       TemporaryStats=False, PreventDirectAttackThisTurn=False,
                       YokaiGainArchetype="", YokaiGainAmount=0, TransformDefinitionId=0)
EFFECT_DEFAULTS = dict(Trigger="None", ActivationZone="Field", Costs=[], PerTurnLimit=0,
                       RequiredGraveyardArchetype="", MinGraveyardCount=0,
                       RequireGraveyardEntryThisTurn="")


def fields(value, allowed, required, context):
    if not isinstance(value, dict):
        raise ValueError(f"{context}: expected object")
    unknown, missing = set(value) - set(allowed), set(required) - set(value)
    if unknown or missing:
        raise ValueError(f"{context}: unknown fields {sorted(unknown)}, missing fields {sorted(missing)}")


def number(value, minimum, maximum, context):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"{context}: expected integer {minimum}..{maximum}, got {value!r}")


def enum(value, values, context):
    if not isinstance(value, str) or value not in values:
        raise ValueError(f"{context}: unsupported enum {value!r}")


def rule(value, context):
    fields(value, {"Category", *TARGET_DEFAULTS}, {"Category"}, context)
    enum(value["Category"], CATEGORIES, context + ".Category")
    zones = value.get("Zones", ["Field"])
    if not isinstance(zones, list) or not zones:
        raise ValueError(f"{context}.Zones: expected nonempty unique zones")
    for zone in zones:
        enum(zone, ZONES, context + ".Zones")
    if len(set(zones)) != len(zones):
        raise ValueError(f"{context}.Zones: duplicate zones")
    enum(value.get("CardType", "Monster"), {"Monster", "Spell", "Any"}, context + ".CardType")
    minimum, maximum = value.get("MinCount", 1), value.get("MaxCount", 1)
    number(minimum, 0, 128, context + ".MinCount")
    number(maximum, 1, 128, context + ".MaxCount")
    if minimum > maximum:
        raise ValueError(f"{context}: MinCount exceeds MaxCount")
    number(value.get("MaxAttack", -1), -1, 100000, context + ".MaxAttack")
    for key in ("RequiredTrait", "RequiredArchetype"):
        if key in value and (not isinstance(value[key], str) or not value[key].strip()):
            raise ValueError(f"{context}.{key}: expected nonempty text")
    category = value["Category"]
    required_zone = {"FriendlyDeck": "Deck", "FriendlyHand": "Hand",
                     "FriendlyGraveyard": "Graveyard", "EnemyGraveyard": "Graveyard",
                     "FriendlyMonster": "Field", "EnemyMonster": "Field", "AnyMonster": "Field"}.get(category)
    if required_zone and zones != [required_zone]:
        raise ValueError(f"{context}: {category} requires {required_zone}")
    if category in {"FriendlyMonster", "EnemyMonster", "AnyMonster"} and value.get("CardType", "Monster") == "Spell":
        raise ValueError(f"{context}: a monster category cannot target spells")


def validate_effects(card):
    context = f"Card {card['Id']}"
    effects = card["Effects"]
    if len(effects) > 16:
        raise ValueError(f"{context}: too many effects")
    ids = set()
    for i, effect in enumerate(effects):
        at = f"{context}.Effects[{i}]"
        fields(effect, {"Id", "Type", "Actions", *EFFECT_DEFAULTS}, {"Id", "Type", "Actions"}, at)
        number(effect["Id"], 1, 1000000, at + ".Id")
        if effect["Id"] in ids:
            raise ValueError(f"{at}: duplicate Effect Id")
        ids.add(effect["Id"])
        enum(effect["Type"], {"Activated", "Triggered"}, at + ".Type")
        enum(effect.get("Trigger", "None"), TRIGGERS, at + ".Trigger")
        if effect["Type"] == "Triggered" and effect.get("Trigger", "None") == "None":
            raise ValueError(f"{at}: a triggered effect requires Trigger")
        enum(effect.get("ActivationZone", "Hand" if card["Type"] == "Spell" else "Field"), {"Hand", "Field"}, at + ".ActivationZone")
        if card["Type"] == "Spell" and effect.get("ActivationZone", "Hand") != "Hand":
            raise ValueError(f"{at}: normal spells activate from Hand")
        for key in ("PerTurnLimit", "MinGraveyardCount"):
            number(effect.get(key, 0), 0, 128, at + "." + key)
        for key in ("RequiredGraveyardArchetype", "RequireGraveyardEntryThisTurn"):
            if key in effect and (not isinstance(effect[key], str) or not effect[key].strip()):
                raise ValueError(f"{at}.{key}: expected nonempty text")
        costs = effect.get("Costs", [])
        if not isinstance(costs, list) or len(costs) > 16:
            raise ValueError(f"{at}.Costs: expected at most 16 costs")
        for j, cost in enumerate(costs):
            where = f"{at}.Costs[{j}]"
            fields(cost, {"Kind", "Amount", "Targets"}, {"Kind"}, where)
            enum(cost["Kind"], COSTS, where + ".Kind")
            if cost["Kind"] in {"Discard", "Tribute"}:
                if "Targets" not in cost:
                    raise ValueError(f"{where}: selection cost requires Targets")
                rule(cost["Targets"], where + ".Targets")
                expected = "FriendlyHand" if cost["Kind"] == "Discard" else "FriendlyMonster"
                if cost["Targets"]["Category"] != expected:
                    raise ValueError(f"{where}: {cost['Kind']} requires {expected}")
            elif "Amount" not in cost:
                raise ValueError(f"{where}: resource cost requires Amount")
            if "Amount" in cost:
                number(cost["Amount"], 0, 100000, where + ".Amount")
        actions = effect["Actions"]
        if not isinstance(actions, list) or not 1 <= len(actions) <= 16:
            raise ValueError(f"{at}.Actions: expected 1..16 actions")
        for j, action in enumerate(actions):
            where = f"{at}.Actions[{j}]"
            fields(action, {"Operation", "Targets", *ACTION_DEFAULTS}, {"Operation", "Targets"}, where)
            enum(action["Operation"], OPERATIONS, where + ".Operation")
            rule(action["Targets"], where + ".Targets")
            for key in ("Amount", "YokaiGainAmount"):
                number(action.get(key, ACTION_DEFAULTS[key]), 0, 100000, where + "." + key)
            for key in ("AttackDelta", "MaxHpDelta"):
                number(action.get(key, 0), -100000, 100000, where + "." + key)
            for key in ("AllTargets", "TemporaryStats", "PreventDirectAttackThisTurn"):
                if key in action and type(action[key]) is not bool:
                    raise ValueError(f"{where}.{key}: expected boolean")
            if action.get("TemporaryStats") and action.get("MaxHpDelta", 0) != 0:
                raise ValueError(f"{where}: temporary maximum HP is unsupported")
            required_zone = {"Search": "Deck", "Salvage": "Graveyard", "SummonFromDeck": "Deck",
                             "SummonFromGraveyard": "Graveyard", "SummonFromHand": "Hand",
                             "Discard": "Hand", "Destroy": "Field", "Transform": "Field"}.get(action["Operation"])
            if required_zone and action["Targets"].get("Zones", ["Field"]) != [required_zone]:
                raise ValueError(f"{where}: {action['Operation']} requires zone {required_zone}")
            if action["Operation"] == "Transform":
                number(action.get("TransformDefinitionId"), 1, 1000000, where + ".TransformDefinitionId")


def expanded_description_data(card):
    """Only documented parser defaults may satisfy a defaulted description path."""
    result = deepcopy(card)
    for effect in result["Effects"]:
        for key, value in EFFECT_DEFAULTS.items():
            effect.setdefault(key, deepcopy(value))
        for item in effect["Actions"]:
            for key, value in ACTION_DEFAULTS.items():
                item.setdefault(key, value)
        for item in [*effect["Actions"], *effect["Costs"]]:
            if "Targets" in item:
                for key, value in TARGET_DEFAULTS.items():
                    item["Targets"].setdefault(key, deepcopy(value))
    return result
