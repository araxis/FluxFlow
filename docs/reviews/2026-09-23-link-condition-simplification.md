# Link conditions without configuration parameters

The user approved removing separately configured link Parameters. Constants now
belong directly in Condition; dynamic values use the existing message context.
This is an intentional feature removal, not an invisible compatibility change.

## Changes

- LinkConfiguration no longer contains Parameters. Old fields are rejected by the
  existing strict link reader, with the component/port location and atomic registration.
- Deleted ConditionParameters and ConfigurationJsonReader. The latter's only remaining
  consumer was link parameter parsing; no replacement JSON converter was introduced.
- Link identity uses canonical source/target, condition text and engine. Parameter
  serialization and signatures are gone; policy conflict checks and keyed DI aliases remain.
- Replaced ApplicationConditionCompiler with the smaller LinkConditionCompiler.
  Engines resolve through native DI without a parallel engine lookup dictionary.
  The single compilation cache uses an engine/text tuple; the old CacheKey type,
  input-type key, conversion and per-message parameter merging are removed.
- Retained reuse of successful compilations and original compilation exceptions
  within one registration session/provider. Each message still evaluates independently.
  Custom engines still receive the fixed input/payload/message type hints. The shared
  expression-engine API remains unchanged for independent consumers.

Input/output declarations, mixed link arrays, relative/full addresses, default and
explicit engine selection, fallback, delivery policies, registration snapshots and
standard value/header/ID/timestamp access remain. No Core/component change, new
configuration model, catalog or runtime manager was introduced.

## Migration

Replace a Condition such as `value.amount > minimum` plus Parameters.minimum = 10
with `value.amount > 10`. JSONata uses `$value.amount > 10`. Rebuild callers using
LinkConfiguration's changed constructor or deconstruction. The field is rejected even
when empty; it is never silently ignored.

Port naming/address value types and ExpressionSource support for links are separate
follow-ups, not part of this change. Link Condition remains inline text.

## Validation

Configuration tests cover rejection of removed fields from both directions and typed
DTO reads, different inline thresholds, registration snapshots, shared compilation,
cached failure identity and provider isolation. Existing native runtime and bundled
C#/JSONata integration coverage remains.

- Configuration: 228 passing tests per target (.NET 8 and .NET 10), 456 executions.
- Full Release solution: 1,954 passing test executions, zero failures.
- Public API baseline updated and verified for the intentional LinkConfiguration change.
- Isolated packaged Configuration consumer passed on .NET 8.0.31 and .NET 10.0.12.
- Diff whitespace check passed. No commit, push, merge or package publication.

Logs: artifacts/cleanup-review-20260920/link-simplification-{tests,full-tests,api,package}.log.
