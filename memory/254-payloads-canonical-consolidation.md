# Payloads Canonical Consolidation

Date: 2026-07-23

## Status

The Payloads family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`217-vnext-payloads-flowcontent.md`. Payloads now has one maintained runtime
and Composition contract.

## Canonical Contract

- `PayloadInspectNode` consumes `FlowMessage<FlowContent>` and emits
  `FlowMessage<FlowResult<PayloadInspectionResult>>` through one normal
  broadcast Output.
- The inspection value preserves the exact `FlowContent` instance and exposes
  its cached decoded `FlowValue` so downstream components do not repeat
  decoding.
- Declared JSON, XML, text, and unknown/binary media conventions remain
  deterministic. Missing, invalid, and unsupported text encodings fall back
  to UTF-8 through the canonical codec path.
- Input, preview, and formatted-output limits, JSON/XML formatting, Base64
  detection, host-owned codecs, deterministic clocks, message lineage,
  diagnostics, completion, and fan-out remain preserved.
- Expected size, decode, parse, null-content, and inspection failures are
  normal `FlowResult` values. The node continues processing later messages;
  it has no universal Errors output.
- Composition retains `payload.inspect`, its fixed canonical Input/Output
  metadata, flat options, Designer hints, and optional exact
  `Resources.{name}` codec-catalog and clock references.

## Removed Compatibility Surface

- Removed `PayloadInspectionRequest` and its per-message byte/text wrapper.
  Callers now place exact bytes, content type, and encoding on immutable
  `FlowContent`.
- Removed the duplicate request-based inspection pipeline and universal Errors
  stream.
- Removed the temporary `FlowContentInspectNode` name; the concise
  `PayloadInspectNode` now owns the canonical contract.
- Removed numeric `PayloadErrorCodes`; consumers route on `FlowResult.Kind`,
  `IsError`, and stable string `Error.Code` values.
- Consolidated duplicate runtime tests into one canonical suite and retained
  explicit coverage for JSON shapes, charset handling, empty/binary/value
  content, preview limits, Base64/XML formatting, option validation, events,
  failure continuation, caching, lineage, and fan-out.

## Versioning And Compatibility

- `FluxFlow.Components.Payloads` moves from the local additive `4.0.0`
  milestone to `5.0.0`. Its preceding published baseline is `3.0.1`.
- `FluxFlow.Components.Payloads.Composition` remains `2.0.0`: its public
  registration, type name, ports, resources, and metadata contract did not
  change. Its preceding published baseline is `1.4.0`.
- Source-declaration baseline manifest index 31 changed from 60 to 47 public
  declarations. Composition index 32 remains 12 declarations.
- SDK package validation against runtime `3.0.1` reports only the expected
  request/error type removals and concise-node base/constructor changes on
  net8.0 and net10.0.
- SDK package validation confirms Composition `2.0.0` remains binary
  compatible with published `1.4.0`.
- No API compatibility suppression was generated.

## Verification

- Payloads runtime tests: 19 passed with no warnings on the final no-build run.
- Payloads Composition tests: 13 passed with no warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed.
- Designer tests: 112 passed.
- Release tests: 96 passed with no warnings.
- Controlled Debug build: succeeded with no errors; a subsequent controlled
  incremental check was warning-free.
- Controlled Release build: succeeded with no errors. A full warning-audit
  rebuild separated 40 existing legacy Composition obsoletion warnings from
  two new nullable test assertions; those assertions were fixed, and the final
  clean Payloads rebuild is warning-free. No new warnings remain.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for runtime `5.0.0` and
  unchanged Composition `2.0.0`, including archive inspection, isolated smoke
  restore/build, and feed checks.
- A package-only net8.0 consumer restored both packages, built with warnings as
  errors, exercised failure-as-data continuation and canonical registration
  metadata, and printed `PAYLOADS_CANONICAL_API_OK`.
- The ignored local graph was refreshed to 16,077 nodes, 34,572 edges, and 938
  communities after implementation and memory updates.

## Next Gate

Audit the unresolved Observability generic compatibility entry before removal.
Preserve counter predicate/expression behavior, logger template and attribute
selection, metrics size selection, host resources, normal result variants,
diagnostics, fan-out, and Composition metadata before consolidating names and
contracts.
