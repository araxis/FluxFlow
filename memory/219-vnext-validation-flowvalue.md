# vNext FlowValue Validation

Date: 2026-07-19

## Status

The sixteenth bounded vNext milestone is implemented on local branch
`work/validation-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone gives Validation a canonical `FlowValue` contract while
preserving its existing generic standalone node, selector, result, branch
ports, and generic Composition registration as compatibility surfaces.

## Canonical Node Contract

- `FlowValueJsonSchemaValidatorNode` consumes `FlowValue` and emits one
  `FlowResult<JsonSchemaFlowValueValidationResult>` through Output, plus
  lifecycle and message diagnostics through Events.
- Valid and invalid schema evaluations are both successful result variants.
  Schema rejection is workflow data, not a component error.
- Missing input, selector failure, and schema evaluation failure are stable
  normal error results. Unexpected implementation faults remain observable
  through node completion.
- The result preserves the exact input and selected `FlowValue`, schema and
  selector metadata, timestamp, validity, and validation issues. Message
  lineage preserves correlation, trace, and headers while causation identifies
  the consumed message.
- Canonical Validation does not expose Valid, Invalid, or Errors ports. The
  existing generic node retains those declarations for code-authored
  compatibility.

## Value And Schema Semantics

- `IJsonSchemaFlowValueSelector` provides a transport-neutral selector boundary
  that returns `FlowValue` directly.
- Schema evaluation converts `FlowValue` deterministically to ordinary JSON
  semantics. Object members use ordinal ordering; binary data uses Base64;
  temporal, duration, and GUID values use invariant strings.
- The canonical input is deliberately `FlowValue`. Content parsing remains an
  explicit Serialization concern, so Validation does not infer a
  `FlowContent` conversion.
- `FlowResult<T>` is a real typed payload. Links do not implicitly unwrap a
  successful result into `T`; downstream extraction or routing requires an
  explicit result-aware component or mapper.

## Composition And Designer

- Parameterless `RegisterJsonSchemaValidator()` registers the canonical fixed
  node. Existing `RegisterJsonSchemaValidator<TInput>(...)` remains available
  for explicit generic compatibility use.
- Canonical Designer metadata describes one `FlowValue` Input and one
  `FlowResult<JsonSchemaFlowValueValidationResult>` Output, with no legacy
  branch or universal error ports.
- The optional host-owned selector and clock retain their picker kinds and use
  canonical `Resources.{name}` addresses.
- Package documentation uses flat `Resources` and `Workflows` sections and
  documents the typed result boundary explicitly.

## Compatibility And Versioning

- `FluxFlow.Components.Validation` moves from `3.0.2` to `4.0.0` for the
  additive canonical node, selector, result, constants, and converter.
- `FluxFlow.Components.Validation.Composition` moves from `1.4.0` to `2.0.0`
  because its parameterless fixed contract now uses canonical `FlowValue` and
  normal result ports.
- Source-declaration baseline entry 23 is
  `23|87|D3FB8F2A41C5D03E4D55DFF6F290B75AFD9F241FE4238A7D8396C7875822FDAA`.
  Entry 24 is
  `24|15|A332F65407BA142F927CA00D6200A0B1CA11200CF2366C4600B000375F134F14`.
- SDK package validation passes for Validation `4.0.0` against published
  `3.0.2` and Validation Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source and dependency cache outside the
  repository.

## Verification

- Validation runtime tests: 23 passed, including 16 preserved generic tests
  and 7 canonical value, valid/invalid result, error, continuation, selector,
  event, and lineage regressions.
- Validation Composition tests: 18 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Component Composition metadata convention tests: 28 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,021 tests across 63 projects with no
  failures or skips.
- Final controlled Debug and Release solution builds completed with zero
  warnings and zero errors.
- A package-only net8 consumer restored Validation `4.0.0` and Validation
  Composition `2.0.0` from the temporary source, verified valid and invalid
  normal result variants, exact `FlowValue` identity, message lineage, the
  canonical port surface, and canonical Composition metadata, and printed
  `VALIDATION_VNEXT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,483
  nodes, 26,009 edges, and 1,704 communities. `graphify-out/` remains excluded
  from tracked repository state.

## Deferred Boundaries

- Existing generic validation remains available but is not the canonical fixed
  Composition contract.
- Canonical Validation does not own content parsing, implicit result
  extraction, link mapping, or transport policy.
- Assertions, Expectations, and the remaining families retain their current
  contracts until separate bounded migrations.

## Next Gate

Migrate Assertions as the next bounded family. Define normal assertion result
variants over canonical values, preserve existing expression-engine and generic
compatibility where practical, and update its Composition, Designer, and
package surfaces without combining Expectations into the same milestone.
