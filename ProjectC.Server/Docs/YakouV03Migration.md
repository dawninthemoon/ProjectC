# Yakou v0.3 — authoritative server migration plan

Status: **partial implementation; prototype content is NOT live** (2026-11-12).
This document is the server contract for the next stages, not an assertion of functionality.

## Source of truth and compatibility

- Previous [PR #6](https://github.com/dawninthemoon/ProjectC/pull/6) is merged.
- New authoring assets: `ProjectC.Server/Data/YakouPrototype/{Cards,Decks,Localize,GameConfig}.json`.
- Keep `ProjectC.Server/Data/HyakkiYagyo` and Unity published catalogs intact until all
  Yakou effect operations, response windows and game transitions have runtime support.
- Existing `CardCatalog::FromJson` only understands the old effect dialect. The
  `YakouPrototype` loader added in this PR validates the new *authoring* dialect,
  and reports unsupported effects; it does **not** make the catalog playable.
- Version and configuration should be captured once per match, not read mid-match.

## Game rules

| Category | Yakou v0.3 |
| --- | --- |
| Deck / leader | 30 cards / 20 HP |
| Field | Five **fixed** monster slots per player, do not compact empty positions |
| Starting hand | 5 cards |
| Draw | Own turn: top up toward 5, max 3 drawn; at 5+ draw 1; first player's opening turn draws 0 |
| Normal summon | One per own Main, free |
| Other summons | Only the methods listed on each card. Special summons do not use the normal-summon count |
| Spells | No resource cost unless explicitly defined (e.g. Necromancy N / Discard N) |
| Ki (気力) | Start 3, max 3, **zero automatic regeneration**. Not mana |
| Ki actions | Manifest 2 (once/turn), postpone one zero-countdown attacker to 1 for 1 (once/unit/turn), redraw up to three hand cards via deck bottom for 1 (once/turn) |
| Manifest | Own Main, first turn allowed if conditions met; same instance and slot, retain damage/external modifiers/devour bonuses, replace intrinsic effects, countdown -1 (min zero) |
| Attack countdown | Initialize from printed `AttackInterval`. Tick **only at the start of its controller's turn**. If zero at end, mandatory auto-raid (even if summoned this turn by effects) |
| Block | Defender assigns eligible friendly monsters to all attackers in a batch, free. One defender per attacker and one block per defender by default; named abilities can extend |
| Combat | All attackers frozen for raid, resolve in **original field-slot order left-to-right**, damage simultaneous per attacker/blocker pair; monster damage persists |
| Chain | Action activation order 1, 2, 3...; **last-in-first-out** resolution after two consecutive player passes. No interrupts mid-resolution |
| Response windows | Respond to spell activation, successful summon effects (not summon itself), manifestation effects, raid preparation; field abilities as well as hand spells |
| Trigger deferral | Cost-paid and mid-chain triggers queue until current chain ends. Simultaneous trigger registration is active player first, opponent second, owner chooses own order |
| Removal | A registered ability normally resolves even if its original source left the field, unless targets become invalid |
| Exile | No automatic return |

The global prototype default `summon.devourMaterialCount=1` is **not** a cap.
`Devour 2` always requires **exactly two distinct allied field materials**.
`Necromancy 3` requires exactly three cards from the player's graveyard (to Exile).
`Discard 2` requires exactly two **other** cards from the player's hand (to Graveyard).
For a multi-method card, only the methods on its definition are eligible.
Printed devoured ATK/MaxHP bonuses are inherited; its new countdown is its own printed value.

## Unified selection contract (server-side)

- Source zone, owning player, candidate instance IDs and exact quantity all come from
  authoritative server state / card effects. The client never supplies trusted eligibility.
- Reject wrong count, duplicate IDs, non-candidates, cards from another player/zone,
  lost priority, stale requests, or costs that can no longer be paid.
- Until exact N are selected, the UI confirm button stays disabled/grey; the server must
  independently reject invalid selections.
- UI can present eligible **hand spell effects and field monster response abilities in
  the same horizontal scroll list**. It can hide/show that list with an eye icon,
  preserving selections and scroll position; the board remains inspectable.
- No separate summon-method selection dialog for cards with only one defined method;
  field material can be selected directly on the board.
- The response info DTO must include public source, effect description and targets,
  not opponent private hand identities.

## Implementation stages

### A — prototype schema and exact selection guard (this PR)
- Load + validate authoring JSON independently of the old runtime catalog.
- Validate 34 definitions, 30 deck copies, 68 localizations, 72 settings.
- Detect duplicate / missing IDs, illegal manifestation in main deck, invalid cardinalities.
- Explicitly report every effect as **not runtime-enabled**.
- Validate exact count and candidate membership for selection requests.
- Keep original live behavior and CI green.

### B — playable summons and Ki
- Add an explicit Yakou ruleset mode and distinct state, without changing legacy games.
- Fixed field slots, damage/stats layers, Ki counters, own-turn draws.
- Atomic cost payment + separate movement causes and trigger journal for Devour N,
  Necromancy N and Discard N. A failure must leave game state, usage and events unchanged.
- Manifest, postpone, hand redraw. Unit tests including 0/2, 1/2, 2/2, 3/2 selections.

### C — raid and blocking
- Countdown tick, automatic raid selection, batch blocker assignments and eligibility,
  left-to-right resolution and persistent damage. Event ordering must be deterministic.

### D — priority and chain
- Public priority state, pass command and last-in-first-out chain resolution;
  deferred triggers after the active chain, including cost-paid triggers;
  field responder abilities and hand response spells. Reconnect/replay preservation.

### E — effect interpreter, multiplayer integration
- Canonicalize and implement every effect operation in the 34-card prototype
  or fail validation with a precise diagnostic; never execute a partial/empty effect.
- Connect network protocol/MatchSession and projected UI choice data.
- Test `HyakkiSampleV02` 30-card matches, replay and reconnect state hash;
  maintain Ubuntu/Windows/Docker CI and legacy online behavior.

## Decisions that are **not yet final**

- Hand maximum (prior suggestion 8; legacy currently 10).
- Full-field free replacement summons versus Devour exploits.
- Inheritance of *modified* rather than printed stats for Devour.
- Blocking changes when an assigned defender disappears, mid-combat response windows,
  combined Discard+Necromancy cost timing and intricate simultaneous-trigger ordering.
- Ki generation by card effect (basic per-turn regeneration is definitively zero).

Do not silently choose these balance rules. For blocked implementation, document
the safest policy and keep affected feature unavailable until it is explicitly chosen.


## Prototype state-machine implementation (PR #8)

`Network/YakouMatch.{hpp,cpp}` is an isolated, deterministic **server-side
prototype**, not yet the published network match engine. It implements:

- Opening hands, own-turn refill (first player's first-turn draw excluded),
  one normal summon, authored Devour/Discard/Necromancy cost payment,
  printed-stat devour inheritance, preserved five-slot field layout.
- Ki 3/no regeneration; Ki-funded redraw, postponement and manifestation
  (including authored prototype conditions and preservation of devour bonuses).
- Own-turn countdown ticks, automatically declared attackers, defender-owned
  batch blocker assignment, left-to-right combat, persistent damage,
  direct damage when unblocked, and surviving attacker's countdown reset.
- `PendingEffects` records triggered effects; **it does not resolve them**.
  Do not infer that the sample deck is yet playable using its real effects.
- No Unity/MatchSession integration. Legacy player-facing commands are untouched.

`Network/YakouChain.{hpp,cpp}` is likewise an independent priority primitive:
first activation is link 1, following response is link 2, two consecutive
passes lead to last-in-first-out resolution, deferred triggers register active
player first and opponent second. It has **not** been wired to `YakouMatch`
or the full card effect interpreter.

Tests: `YakouMatchTests` and `YakouChainTests` plus the existing
`YakouPrototypeTests`; CI is the source of truth for pass/fail status.
The game is **not yet feature complete**. Remaining critical work includes
full rule-aware effect interpreter, response eligibility and target legality,
mid-raid response windows, reconnect/replay serialization and protocol,
and finally a compatible Unity UI.

### Known decisions still requiring an implementation policy

- New full-field replacement versus Devour must not be assumed to be free
  in the cost solver until the design ambiguity is resolved.
- The live sample includes `OnSummoned`/discard/exile triggers; those are
  recorded by `YakouMatch` but not applied.
- The chain primitive stores links and returns them in resolution order;
  it is **not** a substitute for resolving actions.
- Damage before transformation remains a separate integer counter; effects
  that modify max HP must be layered separately in the future.
- The current prototype raid can resolve immediately following blocker
  assignment; the final design requires inserting the response window there.


## Raid response window (follow-up PR)

- After the defender submits a valid batch of blocker assignments, the prototype
  enters `RaidResponse` rather than permitting immediate combat resolution.
- The defender receives priority first. `PassRaidResponse` rejects calls from
  non-priority players, and combat remains blocked until two consecutive passes.
- This is an **empty response barrier only**. Activation of response spells or
  field abilities, target verification, and execution of resolved chain links
  remain unimplemented; do not call the prototype a playable effect-aware match.
- The existing `YakouChain` LIFO/deferred-trigger primitive remains separate.
  A future implementation must attach an effect interpreter before accepting
  non-empty chain links, otherwise resolving combat would silently skip effects.
- `YakouMatchTests` and `YakouChainTests` include authorization/pass-gate cases.
  Actual CI execution status must be checked on the PR; edits alone are not a
  claim of passing tests.
- Legacy-mode preservation is no longer a user requirement for subsequent
  Yakou migration. Prefer replacing old rule entrypoints after parity tests,
  without deleting useful network/reconnect infrastructure prematurely.
