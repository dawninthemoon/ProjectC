# ProjectC project guide

## Project overview

ProjectC is an online two-player collectible card game with a Unity client and an authoritative C++ server.

- `ProjectC.Client/` is the Unity 6000.0.81f1 client, written in C#. `LobbyScene` handles matchmaking and deck choice, `DeckEditorScene` edits locally stored personal decks, and `BattleScene` presents the match. `ClientApp` keeps the network client alive across scene changes.
- `ProjectC.Server/` is the C++20 server, built with CMake. `Source/` and `Include/ProjectC/` contain the deterministic GameCore; `Network/` contains the WebSocket protocol, match/session management, and server card catalog.
- `ProjectC.Server/Data/` contains server-authoritative card and deck definitions. The Unity client has corresponding presentation/catalog data under `ProjectC.Client/Assets/Resources/CardData/`; clients submit definition IDs, while the server validates and supplies card rules and stats.
- The online protocol is JSON over WebSocket, currently protocol version 2. The server owns match state, command validation, turn progression, replay data, and private-information filtering. Reconnect restores the server snapshot.
- Personal-deck submission and optional server-configured mulligan are implemented. Mulligan is disabled in the base test configuration unless enabled by the selected server data/config. RP/response-chain behavior is not implemented; do not imply it exists or add it as part of unrelated work.
- The default rules/data and the `HyakkiYagyo` experimental rules/data are separate. Keep their catalogs and rule assumptions isolated.

For detailed behavior and operational instructions, consult:

- `ProjectC.Server/Docs/TcgRulesAndEffects.md` for GameCore command, effect, and trigger contracts.
- `ProjectC.Server/Docs/OnlineMultiplayer.md` for server lifecycle, WebSocket protocol, privacy, reconnect, and replay behavior.
- `ProjectC.Server/Docs/PersonalDeckOnline.md` and `ProjectC.Server/Docs/OnlineMulligan.md` for those online flows.
- `ProjectC.Client/Docs/BattleUI.md` and `ProjectC.Server/Docs/LobbyAndDeckEditor.md` for Unity scenes and UI behavior.
- `ProjectC.Server/Docs/HyakkiYagyo.md` for the experimental rules profile.

## Architecture and change boundaries

- Keep game rules in GameCore and network/session concerns in `ProjectC.Server/Network/`. Unity is a presentation and command-submission client, never the authority for a match result or card definition.
- GameCore commands validate input, operate on a candidate state, and commit only successful state changes. Expected rule rejection is returned as a typed `CommandError`; failed commands leave state unchanged.
- Preserve deterministic replay: random seeds/state and accepted commands must reproduce the same results. Do not expose server-private RNG state, hidden card order, or opponent hand/deck contents through DTOs, logs, or errors.
- Treat card JSON and the server catalog as authoritative. Keep client catalog data for display and local deck feedback only; never trust client-submitted effects, stats, or costs.
- Preserve protocol version and serialized/wire field names unless a task explicitly changes the protocol and updates both client and server.
- Keep the base test rules/data separate from `HyakkiYagyo`. Do not change established game rules or card effects as a side effect of refactoring.
- Keep Unity scene/prefab references and serialized asset fields compatible. Do not rename or remove serialized fields without an explicit migration plan.

## Unity client conventions

- Use PascalCase for types, methods, properties, events, and enum members; camelCase for parameters and locals; `_camelCase` for private instance fields.
- Use four spaces, Allman braces, and multi-line control flow. Always brace `if`/`else`, loops, and other control statements, even when the body is one statement.
- For a UI component in the same scene hierarchy, prefer `UnityBindingExtensions.Bind` over `[SerializeField]` references. `Bind` searches descendants by object name, so names must be unique within the lookup root. Use `BindNoRecursive` when the component must be a direct child. Check required bindings during initialization and report a clear setup error when one is missing.
- Use `[SerializeField] private` for references that are not in the component's lookup hierarchy, such as assets, configuration objects, or intentionally assigned prefab references. Do not use hierarchy binding for those external assets.
- Keep UI presentation, user interaction, deck persistence, and network transport responsibilities in their existing components. Do not put game-rule authority in a presenter or view.
- Use exceptions for invalid input/configuration and Unity logging for recoverable runtime/setup diagnostics; do not silently swallow errors.

## C++20 server conventions

- Use PascalCase for types, scoped enums, and established GameCore/Network members; use camelCase for parameters and local variables.
- Use explicit includes, `#pragma once`, and `enum class`. Use `Type const&` for read-only references, raw pointers only for non-owning nullable access, and smart pointers for ownership.
- Use four spaces, Allman braces, and multi-line control flow. Always brace control statements, including single-statement bodies.
- Return typed command errors for expected game-rule rejection. Reserve exceptions for parsing failures, invalid configuration, and unrecoverable server errors.
- Keep network parsing strict: validate message shape, size, identity, match, sequence, and authorization before applying commands.
- Comments should explain non-obvious authority boundaries, privacy rules, replay determinism, and state invariants.

## Constants and compatibility

- Do not leave magic numbers for limits, capacities, protocol values, timeouts, or game-rule parameters. Name them at the narrowest useful scope with a name that communicates their meaning (`constexpr`, `const`, or `static readonly`).
- Keep intrinsic arithmetic values inline when their meaning is unambiguous, such as an index increment or a bit width used by a documented encoding. Test fixture values and UI-authoring coordinates/colors may remain inline when the values themselves are the data being described.
- Keep protocol values, game rules, serialized data, and user-facing validation messages unchanged during style-only edits.

## Formatting and verification

- Follow the repository `.editorconfig` for C# and `.clang-format` for C++.
- Do not reformat generated Unity project files or generated assets. Restrict broad formatting to tracked source files.
- Do not add or run tests unless the task asks for tests or verification. When verification is requested, keep existing tests intact and report exactly what ran.
- Do not create commits or push changes unless explicitly requested.
