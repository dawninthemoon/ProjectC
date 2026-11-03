import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import ExportCardData as export


class ExportTests(unittest.TestCase):
    def setUp(self):
        self.rows = export.load(export.CARD_SOURCE)
        self.translations = export.load(export.LOCALIZE_SOURCE)
        self.decks = export.load(export.DECK_SOURCE)
        self.existing = export.load(export.SERVER / "Decks.json")

    def catalog(self):
        return json.loads(export.generate(self.rows, self.translations))

    def test_all_effect_json_and_original_deck_config_are_preserved(self):
        catalog = self.catalog()
        by_id = {card["Id"]: card for card in catalog["Cards"]}
        for row in self.rows:
            self.assertEqual(json.loads(row["Effects"]), by_id[int(row["Id"])]["Effects"])
        merged = json.loads(export.generate_decks(catalog, self.decks, self.existing))
        self.assertEqual(self.existing["Config"], merged["Config"])
        originals = {deck["Id"]: deck for deck in self.existing["Decks"]}
        result = {deck["Id"]: deck for deck in merged["Decks"]}
        self.assertEqual(originals["HyakkiYagyo"], result["HyakkiYagyo"])
        for name in ("HyakkiSample", "CrimsonSample", "FrostSample"):
            self.assertEqual(30, len(result[name]["Cards"]))
            self.assertLessEqual(max(result[name]["Cards"].count(card) for card in result[name]["Cards"]), 3)
        self.assertEqual(merged, json.loads(export.generate_decks(catalog, self.decks, merged)))

    def test_invalid_effect_enums_costs_targets_and_fields_are_rejected(self):
        mutations = [
            lambda e: e.update(Type="Continuous"),
            lambda e: e.update(Trigger="OnTypo"),
            lambda e: e["Actions"][0].update(Operation="Unsupported"),
            lambda e: e["Actions"][0]["Targets"].update(Category="EnemyDeck"),
            lambda e: e["Actions"][0]["Targets"].update(Zones=["Graveyard"]),
            lambda e: e["Actions"][0]["Targets"].update(CardType="Spell"),
            lambda e: e["Actions"][0]["Targets"].update(MinCount=2, MaxCount=1),
            lambda e: e["Costs"][0].update(Kind="FreeTypo"),
            lambda e: e["Costs"][0].update(Amount=-1),
            lambda e: e["Costs"][0].pop("Amount"),
            lambda e: e["Costs"][0].update(Kind="Discard"),
            lambda e: e["Actions"][0].update(Amunt=99),
        ]
        original = copy.deepcopy(self.rows)
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                self.rows = copy.deepcopy(original)
                row = next(row for row in self.rows if int(row["Id"]) == 3003)
                effects = json.loads(row["Effects"])
                mutate(effects[0])
                row["Effects"] = json.dumps(effects)
                with self.assertRaises(ValueError):
                    self.catalog()

    def test_duplicate_ids_and_unknown_references_are_rejected(self):
        self.rows.append(copy.deepcopy(self.rows[0]))
        with self.assertRaisesRegex(ValueError, "Duplicate card"):
            self.catalog()
        self.rows.pop()
        row = next(row for row in self.rows if int(row["Id"]) == 1006)
        row["AllowedManifestations"] = "[999999]"
        with self.assertRaisesRegex(ValueError, "AllowedManifestations"):
            self.catalog()

    def test_localization_and_description_index_errors_are_rejected(self):
        row = next(row for row in self.rows if int(row["Id"]) == 3003)
        row["NameKey"] = "missing_name"
        with self.assertRaisesRegex(ValueError, "missing_name"):
            self.catalog()
        row["NameKey"] = "card_3003_name"
        row["DescriptionArgs"] = '[{"path":"Effects[0].Actions[999].Amount","default":2}]'
        with self.assertRaises(ValueError):
            self.catalog()
        row["DescriptionArgs"] = '[{"path":"Effects[0].Actions[0].Amunt","default":2}]'
        with self.assertRaises(ValueError):
            self.catalog()
        row["DescriptionArgs"] = '[{"path":"Effects[0].Actions[0].Amount"}]'
        text = next(item for item in self.translations if item["lmk"] == "card_3003_desc")
        text["korean"] = "Damage {1}"
        with self.assertRaisesRegex(ValueError, "card_3003_desc"):
            self.catalog()
        text["korean"] = "Damage {0} {Monster:@MissingKeyword}"
        with self.assertRaisesRegex(ValueError, "keyword_MissingKeyword"):
            self.catalog()
        text["korean"] = "Damage {0}"
        generated = next(c for c in self.catalog()["Cards"] if c["Id"] == 3003)
        self.assertEqual(2, export.resolve(generated, generated["DescriptionArgs"][0]))

    def test_deck_errors_identify_the_export_and_row(self):
        catalog = self.catalog()
        mutations = [lambda r: r[0].update(CardId="999999"),
                     lambda r: r[0].update(CardId="1007"),
                     lambda r: r[0].update({"\uB9E4\uC218": "4"}),
                     lambda r: r.append(copy.deepcopy(r[0])),
                     lambda r: r.pop(0)]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                rows = copy.deepcopy(self.decks)
                mutate(rows)
                with self.assertRaises(ValueError):
                    export.generate_decks(catalog, rows, self.existing)

    def test_new_authoring_descriptions_are_not_rewritten(self):
        catalog = self.catalog()
        source_by_id = {int(row["Id"]): row for row in self.rows}
        for card in catalog["Cards"]:
            if 3001 <= card["Id"] <= 4006:
                expected = json.loads(source_by_id[card["Id"]]["DescriptionArgs"])
                self.assertEqual(expected, card["DescriptionArgs"])


if __name__ == "__main__":
    unittest.main()
