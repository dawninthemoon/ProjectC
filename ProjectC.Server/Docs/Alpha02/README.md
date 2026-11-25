# ProjectC Alpha 02 — staged experimental catalog

Source: ProjectC_Data, Alpha02_CardData / Alpha02_DeckData / Alpha02_Localize /
Alpha02_GameConfig / ResourceDefinitions / CostDefinitions.
91 card definitions (36 new), six 30-card decks. Active Alpha01 files unchanged.
The actual staged catalog lives at Data/Alpha02/AuthoringSnapshot.json.

## Local development
Run ProjectCNetworkServer --yakou-prototype on --alpha02 on
(with --data pointing to ProjectC.Server/Data if required).
The switch is explicit and incompatible with website/ranked mode.
Use Alpha02Onmyoryo and Alpha02BloodOni as sample DeckId values.

## Implemented
- Typed resource and mixed ordinary/resource costs, two-resource deck limit,
  resource definitions, snapshot/hash and legacy Ki compatibility.
- Main spell resolution listeners, positive surviving damage listeners,
  effect damage, one-shot shielding, wounded and devoured-material conditions,
  linked effects, enemy targets, intrinsic combat modifiers.
- New typed raid responses and spirit counterspell; 3009 reduction and
  3017 protection applied in the targeted main-spell response window.
- Two-target blood curse, private definition-based deck search and
  deterministic prototype shuffle. No deck-order IDs are exposed by search.
- Real-catalog test cases for opening Spirit/Blood combos, three-resource
  rejection, existing damage protection and deterministic private search.

## Still required before production
- New CI and real WebSocket/Unity gameplay verification; all 36 card effects
  have code paths, but this is not a per-card verification or balance signoff.
- General summon-success/manifest response chains and unrestricted mixed
  spell/raid response timing are not implemented; this is the current
  authored subset of the rules.
- Prototype shuffle seed uses private match state; replace with recorded
  authoritative RNG state before adversarial or ranked play.
- Ki-only postpone/redraw retained until the non-Ki common action rule is decided.
- New card strings are Korean only; art, UX layout and translations remain.
- Do not merge for production merely because this experimental path loads.
