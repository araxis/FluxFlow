# Mapper setup and optional expression engines

## Outcome

Configuration can build a Mapper using C# or JSONata, from inline expression text or a
file. Engine choice is confined to setup. No Core production code was changed and no
engine dependency was added to the component. The two engine projects appear physically
and in Visual Studio under Expressions. No custom catalog/context/factory class was added.

The component's supplied delegate now receives the original FlowMessage instead of only
FlowValue. This is the one breaking signature change: existing payload-only callbacks
read message.Value. This allows setup to supply expression parameters `value`, `headers`,
`messageId`, `traceId`, `correlationId`, `causationId`, `timestamp`. IDs are strings.
Mapper still calls WithValue itself, preserving correlation/trace/headers with fresh
message identity and input causation. Existing errors bypass mapping; runtime failures
become the existing node.processing_failed output and subsequent messages continue.

Registration binds a data-only MapperConfiguration and registers native factory/port
delegates. Build resolves a keyed ExpressionCompiler, loads text and compiles once.
Failed syntax/files/engine lookup prevents source start and cleans up previous components.
Each Build makes independent components/delegates; no shared mutable evaluation variables.

## Engine behavior and tradeoffs

- C#: DynamicExpresso.Core 2.19.3 with a small read-only DynamicObject JSON/header view.
  It reuses FlowValue.Json, wraps only accessed subobjects, and keeps missing properties
  distinct from null. Nested output views serialize as their underlying JSON, not `{}`.
  Int64 then Decimal then finite Double; literals default to Decimal. Integral decimal
  array indexes are accepted. Keys are case-sensitive and non-identifier keys need indexing.
- JSONata: Jsonata.Net.Native/SystemTextJson 3.0.0. One immutable parsed query; independent
  input DOM/environment per message. Payload is `$` and `$value`; metadata is separate
  bindings. Explicit null is valid, top-level undefined is an error. The existing library
  output bridge uses JSON text then JsonDocument; this is not a zero-copy path.
- C# timestamp is native DateTimeOffset; JSONata timestamp is ISO 8601 text. C# is the
  engine's expression subset, not full C# scripts. Object-array value types can require
  explicit `(object)` casts; arbitrary CLR/object construction is not promised.
- Cancellation checks occur before/after synchronous engine evaluation, not inside it.
  Files resolve against a single host base directory; no search, Base64, or fallback for
  missing files. Extension inference can be overridden with `file:` or `inline:`.
  File contents are captured at Build, not monitored for changes.

The older IFlowExpressionEngine remains used by Core revision/link configuration. It was
not removed, silently repurposed or advertised as migrated. The new focused setup compiler
is ready to reuse for further component setup; merging old paths needs contract review.
No production performance claim is inferred from the previous feasibility microprobe.

## Verification

- Release solution build: zero warnings/errors.
- Full final suite: 2,641 passed, zero failed/skipped, 29 TRX files (60 additional test
  executions over the preceding 2,581 baseline; do not add focused runs to this count).
- Tests cover dynamic nesting/indexing/scalars, decimal precision, non-mutation, metadata,
  concurrent independent bindings, cancellation, runtime recovery, lineage, compile once,
  file handling, independent Builds and partial-construction cleanup.
- API baseline reviewed/accepted. Package manifest, metadata, solution layout and dependency
  order checks updated for optional engines and passed.
- Fresh package-only full pipeline on net8/net10: Scheduler -> Source -> Gate -> JSONata
  Mapper -> C# Mapper -> async sink, with owned resource cleanup after components.
  Eight candidate packages, version 0.0.0-local.20260919224844.
- Separate Mapping/Core package consumer passed net8/net10, including failure recovery
  and lineage with the new delegate signature; version 0.0.0-local.20260919224920.

Evidence: `artifacts/mapper-expressions-20260920/` (build-final.log, verified-tests/,
verified-tests.log, package.log, mapping-package.log, verification.json). Earlier failed
focused/layout/packaging attempts remain separate from final results. Package generation
followed source tests, avoiding mixed package-version binaries during tests.
No commit, push, release publication, external-project changes or external services.

Next bounded slice: component setup that reuses expression compilation (e.g. Router),
with explicit review of the old predicate/revision expression contract before merging it.
