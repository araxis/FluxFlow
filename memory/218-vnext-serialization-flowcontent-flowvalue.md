# vNext FlowContent And FlowValue Serialization

Date: 2026-07-18

## Status

The fifteenth bounded vNext milestone is implemented on local branch
`work/serialization-vnext`. No push, tag, publication, pull request, or merge
was performed.

This milestone migrates canonical Serialization nodes to explicit conversions
between `FlowContent` and `FlowValue` while preserving every existing
request-based standalone node and contract as a compatibility surface.

## Canonical Node Contracts

- `FlowContentJsonParseNode`: `FlowContent` to `FlowResult<FlowValue>`.
- `FlowValueJsonStringifyNode`: `FlowValue` to `FlowResult<FlowContent>`.
- `FlowValueTextEncodeNode`: FlowValue string to `FlowResult<FlowContent>`.
- `FlowContentTextDecodeNode`: `FlowContent` to a FlowValue string result.
- `FlowContentBase64EncodeNode`: exact content bytes to a FlowValue Base64
  string result.
- `FlowValueBase64DecodeNode`: FlowValue Base64 string to binary
  `FlowResult<FlowContent>`.
- Each node has one Input, one normal Output, and Events. Expected format,
  type, size, null-input, and encoding failures are stable `FlowResult`
  variants and do not stop later messages. Unexpected faults remain observable
  through node completion rather than being converted into data.
- Result messages preserve correlation, trace, and headers while `With(...)`
  establishes causation from the consumed message.

## Conversion Semantics

- JSON parse reuses the `FlowContent` decode cache and explicitly applies the
  JSON codec even when the declared media type is generic. Parser options for
  trailing commas and comments remain supported.
- JSON stringify emits deterministic ordinary JSON with ordinal object-property
  ordering. Binary values use Base64 strings; temporal, duration, and GUID
  values use invariant strings.
- Text decode honors `FlowContent.Encoding`, then a content-type charset,
  including quoted values. Unsupported declarations use the configured UTF-8
  fallback, and a matching byte-order preamble is removed.
- Base64 encode operates on exact original bytes. Value-backed content must be
  binary or string data, avoiding hidden object serialization.
- Existing input/output byte limits and configured encoding options apply to
  the canonical nodes without leaking transport or resource ownership into the
  package.

## Composition And Designer

- Serialization Composition registers six fixed canonical node types with
  explicit concrete factories. Inputs are `FlowContent` or `FlowValue`; every
  Output carries `FlowResult<T>` and no fixed node declares a universal Errors
  port.
- The optional host-owned clock retains picker kind `clock` and now uses the
  canonical `Resources.{name}` address pattern.
- Designer metadata describes the canonical port shapes while preserving the
  existing JSON, encoding, runtime-limit, and boolean option hints.
- Package documentation uses flat `Resources` and `Workflows` sections and
  direct component-to-component port addresses.

## Compatibility And Versioning

- `FluxFlow.Components.Serialization` moves from `3.0.1` to `4.0.0` for the
  additive canonical nodes, result/error constants, and shared conversion
  implementation.
- `FluxFlow.Components.Serialization.Composition` moves from `1.4.0` to
  `2.0.0` because its six fixed node contracts now use canonical
  `FlowContent`/`FlowValue` inputs, normal result outputs, and no Errors port.
- Source-declaration baseline entry 35 is now
  `35|188|4D83E653E8D153AE77F069AD00EB03A954ABF2A1DE65EA14433C53960D0EDC7A`.
  Entry 36 remains 21 declarations with source hash
  `F421F2083A8DC2093FAA60A54BC0D4A1738BC144E8A94E12A93D236ED2762A5F`.
- SDK package validation passes for Serialization `4.0.0` against published
  `3.0.1` and Serialization Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source and dependency cache outside the
  repository.

## Verification

- Serialization runtime tests: 33 passed, including the 19 preserved legacy
  tests and 14 canonical conversion, continuation, identity, option, event,
  encoding, size, null-input, and numeric-overflow regressions.
- Serialization Composition tests: 15 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,011 tests across 63 projects with no
  failures or skips.
- Final controlled Debug and Release solution builds completed with zero
  warnings and zero errors.
- A package-only net8 consumer restored Serialization `4.0.0` and Serialization
  Composition `2.0.0` from the temporary source, parsed JSON into `FlowValue`,
  stringified deterministic JSON into `FlowContent`, verified message lineage
  and all six composition registrations, and printed
  `SERIALIZATION_VNEXT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,374
  nodes, 25,828 edges, and 1,687 communities. `graphify-out/` remains excluded
  from tracked repository state.

## Deferred Boundaries

- Canonical Serialization performs explicit conversions only. It does not
  infer conversions on links, mutate `FlowContent`, or own transport/media
  policy.
- Request-based nodes remain available for code-authored migration but are not
  registered by canonical Serialization Composition 2.x.
- Validation, HTTP, and the other remaining component families retain their
  current contracts until separate bounded migrations.

## Next Gate

Migrate Validation as the next bounded family. Define its canonical
`FlowValue`/`FlowContent` input and normal result variants, preserve existing
schema and selector compatibility where practical, and update its
Composition/Designer/package surfaces without combining another family into
the same milestone.
