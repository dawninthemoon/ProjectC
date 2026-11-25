# ProjectC Alpha 02 — staged catalog and special resources

Source: ProjectC_Data, Alpha02_CardData / Alpha02_DeckData / Alpha02_Localize /
Alpha02_GameConfig / ResourceDefinitions / CostDefinitions.
91 card definitions (36 new), six 30-card decks. Alpha01 active files unchanged.
AuthoringSnapshot.json contains the actual staged rows normalized into server JSON.

## This PR implements
- Typed resource costs and a single ordinary selection-zone cost per action (legacy
  authoring remains compatible). Invalid/ambiguous cost shapes fail closed.
- Per-deck discovery of resource IDs from card costs, optional costs and reachable
  manifestations; max two distinct resources. Resource gains do not activate a resource.
- Configured initial/max/own-turn regen, inactive gains ignored, atomic resource payments,
  public resource balances and private deterministic state hash.
- Resource and mixed discard/resource summons, typed main spell payment,
  targetless ModifyResource, typed manifestation payment; legacy Ki mirror retained.
- Explicit experimental opt-in; staged catalog is NOT enabled by default.

## Outstanding before enabling Alpha02
The 36 cards are authoring data, not fully executable yet. Current interpreter still
needs the new event producers, positive/survived damage context, spell resolution
listeners, enemy target choices, multi-action handling, damage/replay semantics,
deterministic deck search, mixed response and general response offer metadata.
The client and public deck export pipeline need adaptation. Do not set
alpha02.experimentalRuntime true for production. Existing Ki-only postpone/redraw
behavior is provisionally retained; non-Ki decks cannot use them pending design.
Only Korean new card strings are authored. Balance and real Unity matches are unverified.
