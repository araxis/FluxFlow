# Goal Prompt: Canonical Authoring, Storage Immutability, And Hot-Path Cleanup

Date: 2026-07-28

## Objective

Execute the next FluxFlow simplification and performance round without losing
runtime behavior. Preserve the existing flat, standard .NET registration style
and immutable-record direction. Breaking API changes are allowed when they
remove redundant authoring paths or make an existing immutability contract
truthful. Do not introduce reflection discovery, assembly scanning, hidden
conventions, nested callback builders, generic framework layers, provider base
classes, or compatibility wrappers.

## Canonical Component Authoring

Keep this as the single complete component registration and authoring path:

```csharp
services
    .AddFluxFlowComponents()
    .AddComponent("component.type", component =>
    {
        // Runtime and designer declaration in one flat callback.
    });
```

Remove the competing `ComponentDesignMetadataBuilder`,
`OptionDesignMetadataFactory`, and `ResourceDesignMetadataFactory` paths. Their
capabilities are already covered by `ComponentRegistrationBuilder`, immutable
metadata records, or both. Do not replace them with another shared builder.

Retain:

- `ComponentRegistrationBuilder` and its one-level action callback;
- immutable designer metadata records;
- the standalone `ComponentDesignMetadataCatalog` constructor for tooling and
  direct immutable metadata scenarios;
- public attribute name/value constants needed by package authors and metadata
  consumers.

Internalize attribute-map construction helpers when only Designer internals
need them. Registration behavior, validation, finalization, ordering, conflict
detection, idempotence, automatic runtime/design catalog registration, and
metadata snapshots must remain unchanged.

Migrate tests according to their intent:

- registration tests use the canonical `AddComponent(...)` path;
- catalog, validator, picker, and persistence tests use direct immutable records
  or narrow test-local factories;
- no broad test builder may recreate the deleted production path;
- release tests must prevent the removed authoring APIs from returning.

## Storage Attribute Immutability

Keep storage options and contracts as immutable records. Change the mutable
attribute surfaces on `StoragePutRequest`, `StorageQueryRequest`,
`StorageRecord`, and `StorageResult` to read-only dictionary contracts backed by
defensive ordinal snapshots.

Required behavior:

- initialization accepts ordinary dictionaries and read-only dictionaries;
- a source dictionary mutation after initialization cannot change the record;
- consumers cannot mutate attributes through the returned property;
- ordinal key comparison is preserved;
- null attributes normalize to an empty read-only dictionary;
- record `with` copies may safely share immutable snapshots;
- JSON binding and durable file/SQL serialization remain compatible;
- put, get, query, delete, expiration, paging, version, and correlation behavior
  remain unchanged.

Consolidate the existing contract snapshot logic. Remove redundant
provider-level attribute copies where the immutable contract now guarantees
safety. Keep only provider-specific file and SQL persistence mechanics. Do not
introduce storage inheritance, a generic repository, a shared provider project,
or backend settings in application-wide options.

## Flow Logger Hot Path

Parse the immutable logger message template once during node construction.
Render precompiled literal and placeholder segments for each message without
rescanning the template, allocating placeholder substrings, or copying selected
attributes into a second lookup dictionary.

Resolve built-in placeholders explicitly: `category`, `input`, `inputType`,
`level`, and `sequence`. Resolve all other placeholders directly against the
selected-attribute dictionary. Cache stable built-in text such as input type and
level names.

Preserve exact behavior for:

- known built-in and selected-attribute placeholders;
- unknown placeholders, which remain literal;
- unmatched opening or closing braces;
- empty and adjacent placeholders;
- substituted text containing braces, which must not be recursively expanded;
- null, `JsonElement`, and invariant `IFormattable` values;
- selector failures, diagnostics, sequencing, and pipeline behavior.

Keep the implementation small and feature-local. Do not add a public template
engine or a generic parsing framework.

## JSON Serializer Option Ownership

Stop constructing `JsonSerializerOptions` on repeatable paths:

- `SerializationConverters` owns one compact and one indented static option
  instance and selects between them per call;
- `ComponentActivationContext` reuses a private static default option instance
  when the caller does not provide custom options;
- `ApplicationDefinitionJson` internally reuses compact and indented options,
  while public `CreateSerializerOptions(...)` continues returning a fresh,
  caller-mutable instance on every call.

Preserve exact JSON shape, compact/indented behavior, size validation, declared
text encoding, custom caller options, and application-definition parsing. Do
not introduce JSON source generation in this round.

## MQTT Trigger Binding

Replace the MQTT trigger composition conversion that currently serializes the
composition record, builds a dictionary, serializes again, and deserializes the
runtime record.

Map explicitly:

- generate `TriggerId` exactly as `WorkflowName.ComponentName`;
- parse `Subscription` as either one `MqttSubscriptionTarget` or an array while
  preserving order;
- copy workflow acknowledgement, broker acknowledgement, outcome timeout, and
  maximum pending messages exactly;
- use the existing MQTT composition serializer settings only for individual
  subscription targets;
- preserve existing validation and error behavior at activation.

Do not change MQTT registration, resource addressing, resource ownership,
controller startup, reconnect behavior, broker/session lifetime, disposal, or
transport adapter semantics.

## Explicit Non-Goals

- Do not optimize registration-time LINQ over `IServiceCollection`.
- Do not split engine, retry, join, or correlation state machines by file size.
- Do not centralize component-family-local presentation helpers merely because
  their shapes resemble one another.
- Do not add storage provider inheritance or a universal backend abstraction.
- Do not alter session keyed registration in this round.
- Do not alter MQTT resource registrars or lifecycle ownership.
- Do not add compatibility shims for removed APIs.
- Do not stage, commit, push, or create a pull request.

## Documentation And Release Surface

Update current documentation, package documentation, changelog, cleanup ledger,
public API baseline, and the memory index only where the implemented changes
require it. Preserve historical changelog entries; add a new current entry
instead of rewriting history. Ensure project-visible names remain neutral.

## Verification

Before completion:

1. Build and run the narrow Designer, Storage, Observability, Serialization,
   MQTT Composition, Composition, and Release tests affected by this work.
2. Verify every explicit behavior above maps to a concrete test and assertion.
3. Perform pseudo-mutation and assertion-quality review on generated or changed
   tests and close material gaps.
4. Run a full non-incremental solution build with zero errors and warnings.
5. Run the full solution test command with zero failures.
6. Run formatting verification, public API baseline validation, removed-symbol
   searches, project-boundary checks, and `git diff --check`.
7. Record exact commands, counts, and test evidence in `.testagent/status.md`
   and append the achieved outcome to this memory entry.

## Completion Standard

The round is complete only when the repository exposes one canonical component
authoring path, storage attributes are immutable snapshots rather than mutable
dictionaries behind `init`, verified repeatable JSON/template allocations are
removed, MQTT trigger binding is explicit, all affected behavior is protected
by strong tests, and the entire solution remains green.

## Outcome

Completed on 2026-07-28.

- Removed `ComponentDesignMetadataBuilder`, `OptionDesignMetadataFactory`, and
  `ResourceDesignMetadataFactory` without a replacement abstraction. Public
  name/value constants remain, while Designer-only attribute map helpers are
  internal.
- `StoragePutRequest`, `StorageQueryRequest`, `StorageRecord`, and
  `StorageResult` now expose defensive ordinal `IReadOnlyDictionary` snapshots.
  FileSystem and SQL-file providers retain only persistence-specific copying.
- `FlowLoggerNode<T>` parses its immutable template once and renders cached
  segments directly. Exact literal, placeholder, selector, formatting,
  diagnostics, and pipeline behavior is covered by repeated-message tests.
- Serialization converters and application JSON use compact/indented cached
  options. Activation contexts share only a private default; public option
  factories and caller-supplied options remain fresh or identity-preserving.
- MQTT trigger composition now maps every runtime option directly and parses
  scalar-or-array subscriptions without a whole-object JSON round trip. MQTT
  registrar, host resources, controller ownership, and lifecycle are unchanged.
- Storage advanced to `7.0.0`; FileSystem Storage and SQL-file Storage advanced
  to `5.0.0`. Documentation, changelog, cleanup ledger, and public API baseline
  reflect the reviewed breaking surface.

## Verification Evidence

- Focused test projects: 386 passed. Evidence and pseudo-mutation mappings are
  recorded in `.testagent/status.md`.
- `dotnet build FluxFlow.sln --no-restore --no-incremental --nologo
  --verbosity:minimal`: 121 projects, zero errors, zero warnings.
- `dotnet test FluxFlow.sln --no-build --no-restore --nologo
  --verbosity:minimal /m:1`: 1,490 tests passed in 58 projects, zero warnings.
- Public API baseline acceptance and subsequent ordinary validation passed; the
  focused baseline run executed two tests successfully.
- `dotnet format FluxFlow.sln --no-restore --verify-no-changes --verbosity
  minimal`, ledger JSON parsing, removed-symbol searches, logger/MQTT hot-path
  searches, package-boundary tests, and `git diff --check` passed.
- The required static source/test pairing scan found 667 source files, 171 test
  files, 439 syntactically paired files, and 228 static gaps. This is a
  syntax-only routing heuristic, not line or branch coverage; DI/extension and
  indirect behavioral tests were reviewed separately.
