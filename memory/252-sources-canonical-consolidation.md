# Sources Canonical Consolidation

Date: 2026-07-23

## Status

The Sources family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`228-vnext-sources-flowvalue.md`. Sources now has one maintained runtime and
Composition contract.

## Canonical Contract

- `GeneratedSourceNode` emits configured immutable `FlowValue` data in order,
  supports scalar or array configuration through Composition, optionally loops
  to `MaxItems`, and completes empty input without output.
- `SequenceSourceNode` emits immutable objects containing `name`, `sequence`,
  `start`, `step`, `timestamp`, and `value`.
- Both nodes retain bounded live fan-out, source one-start lifecycle,
  pre-canceled startup behavior, deterministic initial/inter-item delays,
  fresh message identity, Events, faulted Completion for unexpected failures,
  and no public universal Errors property.
- Immutable payload instances are shared safely across generated-source
  broadcast targets without deep cloning.
- Composition retains canonical `source.items` and `source.sequence` types,
  fixed FlowValue Output, addressable Events, and the explicit hidden
  `source.generated` document migration alias.
- Optional clocks resolve only through exact `Resources.{name}` addresses and
  remain host-owned.

## Removed Compatibility Surface

- Removed generic `GeneratedSourceNode<TOutput>` and the temporary
  `FlowValueGeneratedSourceNode`, `FlowValueGeneratedSourceOptions`, and
  `FlowValueSequenceSourceNode` names.
- Removed typed `SourceSequenceItem`, numeric `SourceErrorCodes`, and the
  redundant FlowValue source pipeline.
- Removed `GeneratedSourceOptions.OutputType` and `ObjectTypeName`; canonical
  output is always `FlowValue`.
- Removed `SourcesTypedRegistrationExtensions`,
  `RegisterGeneratedSource<TOutput>`, and `RegisterSequenceItemSource`.
- Migrated Composition tests from the obsolete Composition runtime to flat
  canonical application definitions, revision hosting, stable ports, and exact
  resource addresses.

Consumers now use concise Sources nodes, convert typed values to immutable
FlowValue at the application boundary, remove `outputType` and typed
registration calls, and observe unexpected failures through Completion and
Events.

## Versioning And Compatibility

- `FluxFlow.Components.Sources` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Sources.Composition` moves from `2.0.0` to `3.0.0`.
- Source-declaration baseline manifest index 19 changed from 98 to 56 public
  declarations; Composition index 20 changed from 16 to 13.
- SDK package validation against runtime `4.0.0` reports only intentional
  removal of typed, temporary, numeric-error, and generic-output declarations,
  plus the concise Sequence source base-contract change on both target
  frameworks.
- SDK package validation against Composition `2.0.0` reports only intentional
  removal of the generic generated-source overload and typed registration
  extension on both target frameworks.
- No API compatibility suppression was generated.

## Verification

- Sources runtime tests: 29 passed with no warnings.
- Sources Composition tests: 23 passed through canonical hosting with no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 5 warnings and no errors.
- Controlled Release build: succeeded with 43 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A package-only `net8.0` consumer restored Sources `5.0.0` and Sources
  Composition `3.0.0`, built with warnings as errors, exercised generated and
  sequence values, checked alias and registration shape, verified the absence
  of Errors, and printed `SOURCES_CANONICAL_API_OK`.
- Runtime, Composition, and both focused test projects pass formatting
  verification.
- The ignored local graph was refreshed after implementation and memory
  updates.

## Next Gate

Audit Serialization independently. Preserve JSON parse/stringify, text and
Base64 conversion, exact content metadata, encoding fallback, size limits,
normal result failures, diagnostics, lineage, and composition behavior before
removing its legacy request and typed compatibility contracts.
