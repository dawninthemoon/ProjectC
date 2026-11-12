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

## Server-authored summon choice descriptors (follow-up)

`YakouMatch::SummonChoices(player, handInstance)` returns only the methods
declared by that card's `SummonMethods`/`SummonCostJson`, using current
authoritative game state. Each method includes its cost kind, exact selection
count, eligible material instance IDs, and prospective summon slots.

- Normal is free and disappears once the turn's normal summon is used.
- Devour selects only controlled field monsters. Occupied slots are advertised
  only when their occupant can be consumed as one of the materials.
- Necromancy selects only the controller's graveyard cards.
- Discard selects only other cards from the controller's hand.
- A method with insufficient eligible materials or no possible destination slot
  is omitted. The final `Summon` command **still validates** all chosen IDs and
  counts; descriptors are not client authority.
- Calling for an opponent's hand instance is rejected. The network layer must
  restrict these descriptors to their owner.

**Scope:** This is a state-dependent backend query, not a WebSocket DTO.
The production `MatchSession` still uses the older rules and the Unity UI
still lacks an authoritative Yakou cost-selection protocol. It is incorrect
to claim these new cost choices already appear in live online matches.

## First executable authored effects (incremental Stage E foundation)

`YakouMatch::ResolvePendingEffect(controller, selectedTarget)` now consumes
one queued triggered effect **only after** validating its authored definition,
controller, effect usage limit and chosen target. It computes changes on a
candidate match and commits once complete. Failed commands leave the pending
queue, journal, card zones, countdowns and usage counters unchanged.

Implemented operations in this first slice:

- `Mill` from the friendly deck top to graveyard, including source-authorized
  card count and a strict insufficient-deck rejection.
- `ModifyCountdown` for `Self`, `FriendlyMonster`, and
  `OtherFriendlyMonster`, with validated target instance and optional decline.
- Card-name/effect per-turn limit validation for effects with
  `PerTurnLimit` and `LimitScope: CardName`.

**Not implemented:** automatic trigger dispatch, response-chain resolution,
target selection requests/priority, deferred-trigger scheduling, or the
remaining operations (including `Salvage`, `SpecialSummon`, damage modifiers,
combat abilities, and `DevouringMonster` targeting). Unsupported operation
or condition shapes fail explicitly and **remain queued**, rather than silently
resolving. This PR is not sufficient to mark any whole sample deck playable.
No game-mode or protocol switch has been made; the production WebSocket
`MatchSession` continues to use the older rule engine.

The initial resolver is invoked explicitly by a server caller. Future
connection to the effect/response scheduler must require validated selection
and maintain active-player/owner trigger ordering before invoking it.

## Server-side effect targets and additional atomic operations

The pending-effect prototype exposes `PendingEffectChoices(controller)` for
server-derived candidate IDs and exact minimum/maximum selection counts. Only
the effect controller can query these candidates; this method is not itself a
WebSocket endpoint. Its supported operations are the same narrow shapes the
resolver understands, and it rejects unsupported condition/operation shapes
instead of returning misleading choices. Missing targets, exhausted limits,
and insufficient mill cards fail closed.

The candidate-copy resolver additionally handles:

- `Salvage` of one card from the controller's graveyard to hand, enforcing the
  authored card-type and archetype filters. The authored graveyard-archetype
  condition is checked when its trigger is registered. Other conditions and
  `ExcludePaidCostCards` remain unavailable until their trigger and payment
  contexts can be preserved correctly.
- `HealAccumulatedDamage` for controlled `Self`, `FriendlyMonster`, and
  `OtherFriendlyMonster` targets, and the event-derived `DevouringMonster`
  target. Healing cannot reduce accumulated damage below zero.
- `SetCountdown` on those same field target modes, with nonnegative values.
  The current sample's `OnDestroyedEnemyByCombat` dispatch is not wired yet;
  the operation test uses the same authored action with a test-only trigger.
- Optional effects declined by their owner do not consume the card-name effect
  allowance. Name-scoped usage is keyed by the authored `NameKey`, rather than
  assuming every definition ID represents a distinct card name.

These functions are still explicitly invoked server prototype APIs. Automatic
trigger scheduling, the remaining operations, chain effects, public protocol,
Unity online integration, reconnect/replay, and complete card coverage remain
unfinished. In particular, the Korean description for card 2002 describes its
salvage as optional, but its authored action currently has no `Optional` field;
this implementation follows the unchanged JSON and does not infer a new rule.

## Field-only blocker limit passive

The authored `IncreaseMaxBlocks` / `Self` / `WhileOnField` passive is evaluated
before summoning a monster, and its printed base is kept separately from the
effective `MaxBlocks`. `AssignBlockers` can therefore authorize two distinct
attackers for card 3010 without trusting a client-supplied limit. Manifestation
replaces the printed base and preserves any existing additive modifier.
Unrecognized continuous effect shapes reject the action before mutation.

This is still prototype-only: no network DTO or response-chain wiring has been
added, and the separately documented missing-blocker combat policy is not
resolved by enabling the passive.

## Additional prototype safety and viewer projection

- Triggered salvage checks the authored graveyard-archetype condition at
  registration. Failed conditions do not consume an effect use. Unknown
  conditions and unknown action fields fail closed.
- Devour triggers retain the consuming card instance in their event context.
  The devoured source's current definition is captured before it leaves the
  field, so its effect is not silently replaced by its original form.
- `LookAndChoose` supports the authored one-card choice among the deck top.
  Only the controller can inspect the candidates. Unchosen cards go to the
  bottom in their existing relative order; an insufficient deck fails closed.
  Spell activation and `OnResolve` scheduling are not implemented yet.
- Public game commands compute on a copied candidate and commit only after
  success. A malformed late trigger condition, for example, cannot partially
  consume a summon, its cost, or an input allowance.
- `ViewerSnapshot(viewer)` is a prototype projection: it omits both deck
  orders and the opponent hand, preserves five public field slots and attack
  counters, shows each player's Ki, and gives the owner their own normal
  summon availability. It is deliberately not a WebSocket message or a
  reconnect checkpoint; pending selections, chains and private server state
  still need explicit durable serialization. The prototype's player IDs 1/2
  need mapping to the existing globally allocated online seat IDs.
- Deck exhaustion and combat now record an explicit winner in this prototype.

These APIs do not implement the effect scheduler, response spells, combat
modifiers, replay/hash, MatchSession integration, or a playable online mode.

## Server-private deterministic replay foundation

`ReplayYakou` reconstructs a fresh prototype match from its initial shuffled
server-private deck orders, first player, and ordered prototype command
records. It replays accepted and rejected commands, preserves their results,
and verifies that rejected commands do not change the full server state. The
result includes the reconstructed `YakouMatch`, so a caller can inspect a
pending effect or a raid response priority after replaying a partial journal.

`ServerStateHash()` fingerprints a canonical schema containing the full card
catalog and configuration, ordered private zones, public fields and stat
layers, IDs, current turn/phase/winner, effect-use counters, pending effects and
their event context, attacks, journal, and all current `YakouChain` fields.
It uses deterministic FNV-1a 64-bit arithmetic as a replay comparison, not as
an authentication or collision-resistant security primitive. Both the private
state and this fingerprint are server-only and must never be sent to clients.
A reference-hash test checks the same fixture on all CI platforms.

This does **not** yet replace `MatchSession`'s legacy replay archive. There is
no persistent Yakou journal, network request deduplication, server restart
checkpoint loader, reconnect DTO or Unity reconnection flow. It only covers the
prototype operations currently represented by `YakouReplayOperation`; future
chain responses, costs and effects must extend both the command journal and
canonical state before they can be called replay-safe.

## Owner-scoped selection projection for future online DTO

`YakouMatch::ViewerSnapshot(viewer)` now includes `Players[viewer].SummonChoices`
when that player is in their own main phase. Each entry is derived from the
authoritative hand and includes `CardId`, `Method`, `SourceZone`,
`RequiredCount`, `Candidates`, and `AvailableSlots`.
The opponent's player object **never** contains these selection candidates.

When the oldest queued effect belongs to the viewer, the top-level
`PendingSelection` provides the corresponding `SourceCardId`, `EffectId`,
`Trigger`, and a `Status` field. `Ready` includes the supported operation,
source zone, minimum and maximum selection count, and candidate instance IDs.
`Unavailable` explicitly indicates that the authored effect is not currently
resolvable through the prototype selection API, without dropping its queue
entry or breaking the viewer projection. The opponent receives no
`PendingSelection`.

**Scope and limits:** This is a read-only projection, not an online command
protocol, scheduler, effect execution, replay command, or reconnect DTO.
No Unity code/scene/prefab or card/localization JSON was changed. Future
MatchSession integration must map seat IDs and authenticate the requesting
viewer, version selection requests, reject stale inputs, and use the
server-side cost/effect validators on submission. Do not expose the canonical
private state or hash via this projection.

## Immediate combat-only triggers in raid resolution

`ResolveRaid` now evaluates the currently authored, combat-scoped
`OnRaid / ModifyAttack(Self, CurrentCombat)` triggers **before** attack damage
is calculated. The supported optional condition checks the controller's
graveyard archetype count. Likewise, a present defending monster's
`OnAssignedAsBlocker / ReduceDamage(Self, CurrentCombat)` is applied to the
incoming combat damage, with the authored card-name per-turn limit enforced.
This affects the actual battle without mutating printed attack or leaving
temporary modifiers on units after the combat. Supported combat-only triggers
are not enqueued a second time.

The full raid command still commits transactionally through the existing
candidate-copy boundary. Unexpected effects, targets, durations or
conditions fail closed **before** committing combat damage. Tests cover both
a secondary-theme raider/blocker pairing and the sample blocker with a
card-name limit, as well as rejection of an unsupported raid operation.

**Important limitation:** combat-only trigger handling presently takes place
at raid resolution, **after the empty response window**. It is not a general
trigger scheduler or response-aware effect stack. New response cards must be
able to see the appropriate announced triggers before resolving the combat;
that future integration must preserve targeting, usage limits, and temporary
modifier semantics. `OnDestroyedEnemyByCombat`, piercing, spell/response
effects and full online `MatchSession` integration remain unimplemented.
No JSON, Google Sheets, Unity scripts, prefabs or scenes were changed.

## Combat victory countdown trigger (server prototype)

A living raider that **actually destroys an assigned enemy blocker through
simultaneous combat damage** now resolves the authored
`OnDestroyedEnemyByCombat / SetCountdown(Self)` trigger after its normal attack
countdown reset. This supports the printed effect on manifestation card 2102.
It does not trigger for direct hero damage, a missing blocker, or a raider
destroyed in that same combat. Printed card-name turn limits are checked in
the same private effect-use map as other supported effects, and the updated
countdown remains part of the existing deterministic server state.

An unknown combat-kill operation, action shape, target, or limit scope fails
closed: `ResolveRaid` uses a copy and commits only after full success, so
failure cannot leave partial hero damage, graveyard mutations, or turn
advancement. Server tests exercise the authored effect shape, the unsupported
action rollback, and the unblocked-no-trigger case.

**Still unfinished:** this is a narrow synchronous combat-only resolution path,
not a general event scheduler. Full priority/chain windows, other battle
trigger semantics and online MatchSession/WebSocket handling remain future
work. No JSON, Google Sheets, Unity scripts, scenes, or prefabs were edited.

## First main-phase spell activation path (prototype-only)

`ActivateMainSpell(controller, ownedHandInstance)` now validates ownership,
active player's Main phase, the printed spell type, and a **completely empty
activation-cost object**. For now it only accepts one authored
`Triggered / OnResolve` effect whose single action is handled by the existing
manual `ResolvePendingEffect` interpreter. It explicitly rejects any paid,
unsupported or multi-action spell rather than consuming resources incorrectly.

On successful activation, the spell moves from its owner's hand to graveyard,
queues exactly one source-preserving pending effect, and can be resolved with
`PendingEffectChoices`/`ResolvePendingEffect`. Activations with unresolved
previous effects, invalid target availability, already-exhausted card-name
limits, or spoofed card ownership are rejected **transactionally**, leaving
the hand, deck, graveyard, queue and journal unchanged. In particular, the
current authored zero-cost effects supported by this path are the deck-mill
spell 2009, friendly countdown spell 3011 and friendly healing spell 3014
(when a valid target exists). Existing spell rules and card data are unchanged.

`YakouReplayOperation::ActivateMainSpell` captures the same server-private
command for deterministic replay and rejection logging. C++ tests cover
ownership and illegal costs, a targetless mill spell, a target-required field
spell, duplicate activation, and replay determinism.

**Important:** this is a **prototype activation entrypoint**, not the final
online spell protocol. The card is moved to the graveyard and its pending
effect becomes resolvable without an opponent response window. That sequencing
is unsuitable for finalized response/negation play and must be replaced or
gated by the authoritative chain lifecycle before enabling online matches.
No WebSocket DTO, Unity action, response priority, spell cancellation, paid
spell cost, or full card coverage is claimed. No JSON, Google Sheets, Unity
scene, prefab, or C# source was modified.

## Opt-in server spell priority and authored negation (limited first slice)

The prototype now has a separate response-aware activation path:
`BeginSpellResponse(player, ownedHandSpell, targetInstance)` uses the same
zero-cost/one-action validation as the legacy `ActivateMainSpell` path, but
holds the original effect in the private `YakouChain` stack instead of
resolving it immediately. The phase is `SpellResponse`, the opposing player
receives first priority, and illegal passes are rejected without mutation.

`RespondWithNegation(opponent, counterspellInstance, discardedInstances)`
currently recognizes **only authored card 3020**. It validates response
priority, actual owner hand, the authored `Discard:2` cost, two distinct
other owned hand instances, the printed `NegateSpellEffect` action, and
card-name effect limits. Paid materials and the response spell move to
the controller's graveyard. The target is the server-owned opening spell,
never a client-supplied target ID. This slice permits one response link,
not arbitrary chained responses.

After two authorized consecutive `PassSpellResponse` calls, the server
takes the chain's LIFO resolution. A resolved counterspell suppresses the
original spell effect, but the original spell remains in the graveyard and
its card-name per-turn usage allowance is consumed. With no counterspell,
the original effect executes through the existing validated selection and
effect resolver. A wrong priority, illegal cost, invalid selected target,
or unsupported effect fails transactionally; failed last-pass resolution
does not mutate the live match. Phase returns to Main when the chain closes.

All three new operations are covered by server-private `YakouReplay`
commands, including rejected operations and full-state deterministic hashes.

**Limitations:** the original `ActivateMainSpell` prototype API remains
available for compatibility and **does not** open a response window; the
new `BeginSpellResponse` path is opt-in and is not exposed to production
MatchSession/WebSocket. Only card 3020 can respond here. The other response
spells, nested counters, raid response windows, deferred triggers, and
automatic chain resolution of multiple effect-bearing links are **not**
implemented. Do not switch live matchmaking to this prototype yet.
No Unity code, scenes, prefabs, JSON, or spreadsheet data were modified.

## First defensive raid response: 3016 FirstDamageThisRaid shield

The server-prototype `RespondWithRaidShield(defender, ownedSpellInstance,
ownedFieldUnit)` activates the authored zero-cost card **3016**, only when
the defending player has priority in `RaidResponse`. It validates the
controller's actual hand, legal friendly field target, exact authored
`ReduceDamage / FriendlyMonster / FirstDamageThisRaid` action, printed
card-name usage limit and one-link response window. The spell moves into
its owner's graveyard; unauthorized priority, spoofed targets, illegal
spells or repeated responses fail atomically.

After two valid consecutive passes, the existing `YakouChain` returns
the response link in resolution order. The server applies a one-shot
raid shield. When the selected monster first receives positive combat
damage in the raid, damage is reduced by its authored `Amount` (floor
zero) and the shield is consumed. Any unused shield expires on turn
transition or game end. This is a temporary server-owned effect, not a
printed stat mutation. When active, shield data is included in the
server-only canonical replay state; empty shields preserve historical
reference replay fixtures. A new replay operation captures defensive
response command inputs.

Tests compare a blocked 3-attack raid with and without card 3016,
validate opponent priority and target authorization, and verify turn
progression after the shielded combat.

**Not complete:** this supports one specific raid-response spell and
one link only. Stacked reductions, other response spell costs, nested
responses, attack modification, blocker reassignment, effect-damage
prevention, and actual WebSocket/Unity match activation still require
separate work. This prototype must not be presented as full response
support. No Unity code, scenes, prefabs, card JSON or Sheets changed.

## Authored paid raid tactics: attack reductions and blocker reassignment

The opt-in server prototype now supports three additional defensive
one-link raid responses in `RespondWithRaidTactic`:

- **2011** `ModifyAttack / EnemyRaidingMonster / CurrentCombat`: validates
  a single other owned hand card as `Discard:1`, moves it to graveyard,
  and reduces the selected opponent raider's combat attack by the authored
  signed delta, with an attack floor of zero.
- **3019** `ModifyAttack / EnemyRaidingMonster / CurrentRaidPhase`: pays
  `Necromancy:2` by exiling exactly two distinct cards from the defender's
  graveyard and applies the authored raid-phase attack modifier to the
  selected opponent attacker. Temporary reductions expire at raid end.
- **3018** `ReassignBlocker / OneBlockedRaid`: pays `Discard:1`,
  requires an existing blocked raid, and changes its blocker at response
  resolution to another living controlled field monster. The server checks
  the replacement's `MaxBlocks` against its current assignments at
  activation **and** resolution, never trusting a client-computed block
  count.

Each validates printed action schema, target, exact cost zone/count,
distinct instance IDs, controller priority, authored card-name turn
limit, and one supported response link before moving cards. The normal
chain double-pass closes the response and applies the modifier or
assignment before combat. All operations use candidate-copy atomicity.
Server-private canonical state includes active attack modifiers and the
replay journal records selected raiders, optional replacement blocker
and paid card IDs. Old empty-modifier replay hashes remain unchanged.

Tests include the complete turn progression through Mill-based
Necromancy material, invalid payment and spoofed targets, actual
damage differences, blocker reassignment, and deterministic replay of
both accepted and rejected commands.

**Not yet supported:** chained or nested multiple raid responses;
other response spell forms, card 2012's NextDamageEvent, automatic
trigger/event scheduling, and production WebSocket/Unity integration.
All of these remain subject to separate implementation and tests.
No Unity files or card/localization JSON were changed.

## Multiple defensive responses per raid (bounded LIFO prototype)

The raid response window now accepts **up to eight supported defensive links**
instead of a single link. After the defender activates any supported raid
tactic or first-damage shield, priority passes to the active player. If the
active player passes, the defender may respond again with another supported
card. Adding that link resets the consecutive pass count. **Only two
consecutive valid passes following the last activation** close the window.

The existing `YakouChain` provides last-in-first-out resolution for the
four already supported defensive cards: 3016 (first-damage shield), 2011
(current-combat attack reduction), 3019 (current-raid attack reduction), and
3018 (blocker reassignment). Effects are applied in reverse activation order;
target availability and block capacity are validated against each intermediate
state. Invalid resolutions reject the closing pass atomically, keeping the
live chain and graveyards unchanged. Attack reductions can accumulate, with
the authored zero floor applied at combat time; shields and attack modifiers
expire at raid end.

`ViewerSnapshot` now exposes the **public** `RaidResponseDepth` count
during raid responses, in addition to the existing priority player ID.
The chain's selected targets and server-private paid-cost material stay
private. Deterministic replay exercises two consecutive defensive links
with a failed out-of-priority insertion, then verifies LIFO resolution and
the combined damage result.

**Explicit limits:** This is still a bounded defensive-only prototype.
The active player cannot activate an opposing response card; only the
defending player can register links (after priority cycles). A general
two-player response stack, spell-speed windows, triggered effects inside
the chain, multi-step automatic effects, online MatchSession/WebSocket,
and Unity client commands are not implemented here. Existing card JSON and
Unity resources are unchanged.
