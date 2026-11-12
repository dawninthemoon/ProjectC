# Yakou v0.2 prototype data

Generated from the `ProjectC_Data` Google Sheet on 2026-11-12.

- `Cards.json`: 34 card definitions (12 HyakkiYagyo cards, 20 neutral cards, 2 manifestation forms).
- `Decks.json`: `HyakkiSampleV02`, 30 copies across 18 distinct card IDs.
- `Localize.json`: 68 Korean name/description keys.
- `GameConfig.json`: 72 JSON-typed prototype rule settings.

These files are **authoring/prototype data**, not a published runtime catalog.
The existing `Tools/ExportCardData.py` consumes `ProjectC.Client/Assets/Resources/ProjectC_Data - *.json`
using the legacy card/effect dialect. Do not copy these files over `Data/HyakkiYagyo` or
`ProjectC.Client/Assets/Resources/CardData/HyakkiCards.json` until the new
runtime command handlers, cost and trigger semantics, automatic raids, response chain,
and effect operations are implemented and tested.

Card IDs and effect JSON are kept as authored; no unsupported effect was silently dropped.
The 30-card deck total, unique card IDs, localization references, and manifestation IDs
were checked against the exported Sheet values.
