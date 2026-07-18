# vNext FlowContent Payload Inspection

Date: 2026-07-18

## Status

The fourteenth bounded vNext milestone is implemented on local branch
`work/payloads-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone migrates canonical Payloads inspection to `FlowContent` and
normal result variants while preserving the existing request-based standalone
node as an explicit compatibility surface.

## Canonical Node Contract

- `FlowContentInspectNode` consumes `FlowMessage<FlowContent>` and emits
  `FlowMessage<FlowResult<PayloadInspectionResult>>` through one normal output.
- Every inspection value retains the exact input `FlowContent` instance and the
  `FlowValue` cached by `FlowContent.ReadAsFlowValue(...)`. Downstream code can
  reuse the decoded value without deserializing the original bytes again.
- Success uses result kind `Inspected`. Size, decode, parse, null-content, and
  unexpected per-message inspection failures use stable result kinds and
  `payload.*` error codes while retaining available inspection data.
- Expected failures do not fault the processor or stop later messages. The
  canonical node exposes `Output` and lifecycle/processing `Events`, with no
  universal Error port.
- Result messages preserve correlation, trace, and headers while `With(...)`
  creates the next message and causation identity.
- `PayloadInspectNode` remains unchanged for code-authored request/result
  compatibility, including its established Errors and Events behavior.

## Content Semantics

- The package-owned catalog selects JSON for `application/json` and `+json`,
  text-backed XML for `application/xml`, `text/xml`, and `+xml`, text for the
  `text/*` family, and binary fallback for unknown or missing media types.
- Inspection trusts declared media types. Bytes are not implicitly sniffed as
  JSON or XML, and unknown content remains binary.
- Missing, invalid, and unsupported text encodings use the canonical UTF-8
  fallback. Host-provided catalogs can add media conventions without changing
  node code or resource ownership.
- Input limits are checked before decoding. Text and formatted previews remain
  independently bounded; optional JSON/XML formatting and base64 detection use
  the existing `PayloadInspectOptions` surface.

## Composition And Designer

- `RegisterPayloadInspect()` now registers `FlowContent` Input and
  `FlowResult<PayloadInspectionResult>` Output for `payload.inspect`.
- The factory resolves optional host-owned keyed `FlowContentCodecCatalog` and
  `TimeProvider` resources. The runtime package owns neither service lifetime.
- Designer metadata describes only the canonical ports, existing option hints,
  and optional `codecs`/`clock` pickers with `Resources.{name}` key patterns.
- Package documentation uses the flat two-section `Resources`/`Workflows`
  definition. Component settings and references remain flat properties.

## Compatibility And Versioning

- `FluxFlow.Components.Payloads` moves from `3.0.1` to `4.0.0` for the additive
  canonical node, result/error-name constants, decoded-content result fields,
  and new payload kind.
- `FluxFlow.Components.Payloads.Composition` moves from `1.4.0` to `2.0.0`
  because the fixed `payload.inspect` port and error-channel contract changes.
- The reviewed source-declaration baseline changes only manifest entries 31
  and 32: Payloads grows from 38 to 60 declarations and Payloads Composition
  from 11 to 12 declarations.
- SDK package validation passes for Payloads `4.0.0` against published `3.0.1`
  and Payloads Composition `2.0.0` against published `1.4.0`; existing binary
  declarations remain available.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source and local dependency cache
  outside the repository.

## Verification

- Payloads runtime tests: 28 passed, including exact content identity, decode
  cache reuse, unknown-media binary fallback, invalid encoding fallback,
  expected failure continuation, size-before-decode, and null-content handling.
- Payloads Composition tests: 13 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 1,998 tests across 63 projects with zero
  warnings and no skipped tests.
- Final controlled Debug and Release solution builds each covered 130 projects
  with zero warnings and zero errors.
- A package-only net8 consumer restored Payloads Composition `2.0.0` and its
  dependencies from the temporary package source, exercised the standalone
  canonical node, activated the factory from flat `Resources`/`Workflows` JSON,
  verified exact `FlowContent` identity, host codec selection, one-time decode,
  and the no-error-port shape, and printed `PAYLOADS_VNEXT_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 16,234 nodes,
  25,546 edges, and 1,671 communities. `graphify-out/` remains excluded from
  tracked repository state.

## Deferred Boundaries

- Payload inspection does not insert itself into links or automatically convert
  typed component contracts. Mapping remains an explicit workflow component.
- The codec catalog does not own transport ingestion, resource lifetime, or
  host media policy. The first decode recorded by `FlowContent` remains the
  authoritative cached value or failure.
- The legacy request-based node remains for migration but is not registered by
  canonical Payloads Composition 2.x.
- The other normal component families retain their existing contracts until
  migrated in separate bounded milestones.

## Next Gate

Migrate Serialization as the next bounded family. Provide explicit conversions
among `FlowContent`, `FlowValue`, text, JSON, and Base64, keep conversion
failures as normal result data, and update its Composition/Designer/package
surfaces without combining Validation or HTTP into the same milestone.
