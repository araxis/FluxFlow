# Serialization Canonical Consolidation

Date: 2026-07-23

## Status

The Serialization family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`218-vnext-serialization-flowcontent-flowvalue.md`. Serialization now has one
maintained runtime and Composition contract.

## Canonical Contract

- `JsonParseNode`: `FlowContent` to `FlowResult<FlowValue>`.
- `JsonStringifyNode`: `FlowValue` to `FlowResult<FlowContent>`.
- `TextEncodeNode`: string `FlowValue` to `FlowResult<FlowContent>`.
- `TextDecodeNode`: `FlowContent` to string `FlowResult<FlowValue>`.
- `Base64EncodeNode`: exact `FlowContent` bytes to Base64
  `FlowResult<FlowValue>`.
- `Base64DecodeNode`: Base64 string `FlowValue` to binary
  `FlowResult<FlowContent>`.
- Every node has one bounded Input, one broadcast Output, Events, and
  Completion. Expected type, format, encoding, and size failures are normal
  result values; unexpected failures remain lifecycle faults.
- Correlation, trace, headers, causation, deterministic clocks, later-input
  continuation, and immutable broadcast sharing remain preserved.
- Composition retains all six fixed component type names, canonical ports,
  flat options, Designer metadata, and the optional exact
  `Resources.{name}` clock reference.

## Removed Compatibility Surface

- Removed all six request/result DTO pairs and their request-based runtime
  implementations.
- Removed temporary `FlowContent*` and `FlowValue*` canonical node names; the
  concise operation names now own the canonical contracts.
- Removed public `SerializationTransformNode<TInput,TOutput>` and
  `FlowSerializationNode<TInput,TOutput>` bases, duplicate converter and
  exception paths, numeric `SerializationErrorCodes`, and universal Errors
  outputs.
- Replaced duplicate runtime plumbing with one internal result pipeline.
- Migrated Composition tests from the obsolete Composition runtime to flat
  canonical application definitions, revision hosting, stable ports, and exact
  resource addresses.

Consumers use concise operation nodes with `FlowContent` or `FlowValue`, route
on `FlowResult.Kind`, `IsError`, and `Error.Code`, and compose text/Base64
operations where old request fields selected multiple conversions.

## Versioning And Compatibility

- `FluxFlow.Components.Serialization` moves from the local additive `4.0.0`
  milestone to `5.0.0`. Its preceding published baseline is `3.0.1`.
- `FluxFlow.Components.Serialization.Composition` remains `2.0.0`: its public
  registrations, type names, ports, resources, and metadata contract did not
  change. Its preceding published baseline is `1.4.0`.
- Source-declaration baseline manifest index 35 changed from 188 to 106 public
  declarations. Composition index 36 remains 21 declarations.
- SDK package validation against runtime `3.0.1` reports only expected CP0001
  removals and CP0007 concise-node base-contract changes on net8.0 and net10.0.
- SDK package validation confirms Composition `2.0.0` remains binary compatible
  with published `1.4.0`.
- No API compatibility suppression was generated.

## Verification

- Serialization runtime tests: 17 passed with no warnings.
- Serialization Composition tests: 15 passed through canonical hosting with no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed with no warnings.
- Controlled Debug build: succeeded with 3 warnings and no errors.
- Controlled Release build: succeeded with 41 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for runtime `5.0.0` and
  unchanged Composition `2.0.0`, including archive inspection, isolated smoke
  restore/build, and feed checks.
- A package-only net8.0 consumer restored both packages, built with warnings as
  errors, exercised all six runtime operations and registrations, and printed
  `SERIALIZATION_CANONICAL_API_OK`.
- Runtime, Composition, and both focused test projects pass formatting
  verification.
- The ignored local graph was refreshed to 16,127 nodes, 34,726 edges, and 933
  communities after implementation and memory updates.

## Next Gate

Return to the unresolved Payloads request-compatibility ledger entry before
starting Observability. Preserve exact content inspection, cached decoded
values, preview/formatting limits, normal result failures, diagnostics, and
Composition behavior before removing its request DTO and temporary node name.
