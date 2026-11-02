# ProjectC coding conventions

## General

- Keep Unity C# client conventions and C++20 server conventions separate.
- Use four spaces for indentation and format control flow across multiple lines.
- Always use braces for `if`, `else`, loops, and other control statements, including single statements.
- Do not leave magic numbers: give limits, capacities, protocol values, timeouts, and game-rule values named constants at the narrowest appropriate scope.
- Keep protocol values, game rules, and serialized data unchanged during style-only edits.
- Comments should explain non-obvious constraints, authority boundaries, lifecycle requirements, or design reasons.

## Unity C# client

- Use PascalCase for types, methods, properties, events, and enum members.
- Use camelCase for parameters and local variables; use _camelCase for private instance fields.
- Prefer private serialized Unity references with `[SerializeField]`; expose properties when other classes need access.
- Put required `using` directives at the top of each file and keep one primary type per file where practical.
- Use explicit braces and multi-line formatting for control flow.
- Use clear exceptions for invalid input/configuration and Unity logging for reportable runtime conditions.

## C++20 server

- Use PascalCase for types, scoped enums, and established GameCore/Network members; use camelCase for parameters and locals.
- Use explicit includes and scoped enums (`enum class`).
- Use `Type const&` for read-only references, raw pointers for non-owning nullable access, and smart pointers for ownership.
- Return command error values for expected game-rule rejection; reserve exceptions for parsing, invalid configuration, and unrecoverable server errors.
- Preserve the GameCore copy/validate/commit pattern and document replay, privacy, and authority invariants.

## Constants and compatibility

- Name values that represent limits, protocol versions, timeouts, capacities, and game-rule parameters.
- Use the narrowest suitable scope (`constexpr`, `const`, or `static readonly`) and a name that states the value's meaning.
- Keep intrinsic arithmetic values inline when their meaning is unambiguous (for example, incrementing an index or shifting by a documented bit width); use named constants for values that encode a policy or boundary.
- Keep test fixture values and UI authoring coordinates/colors inline when they are the data being described; do not turn those values into unrelated global constants.
- Preserve wire DTO names, serialized field names, and externally visible protocol values unless a task explicitly changes them.
