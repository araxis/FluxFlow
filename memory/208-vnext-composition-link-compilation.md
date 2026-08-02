# vNext Composition Link Compilation

Date: 2026-07-17

## Status

The third bounded vNext milestone is implemented on local branch
`work/composition-links-vnext`. No push, tag, publication, pull request, or
merge was performed.

This milestone stops at parsing, canonicalization, condition compilation, and
static validation. It does not activate links, add stable runtime ports or
direct port APIs, change Engine or Hosting execution, introduce runtime
revisions, migrate component families, or redesign MQTT.

## Link Contract

- Added `FluxFlow.Composition.Links.ApplicationLinkCompiler` over the canonical
  `ApplicationDefinition` and existing `CompositionNodeRegistry` metadata.
- Input and output port properties accept one string, one object with exact
  `Port` and optional `Condition` names, an empty array, or a mixed array of
  string and object declarations.
- Direction is inferred from registered input/output metadata. Successful links
  use absolute `ApplicationAddress` source and target values and preserve
  `ApplicationLinkDeclarationSide` for later Designer persistence.
- Component settings that do not match a registered port remain ordinary
  settings. A property matching both an input and output is rejected as
  ambiguous.
- Local, absolute, and cross-workflow references share the canonical ordinal,
  case-sensitive resolver. Reserved system outputs are source-only.
- Successful links are sorted deterministically. Multiple upstreams and
  fan-out are permitted by default, and no mapper is inserted.

## Conditions And Validation

- Composition now references the dependency-free `FluxFlow.Mapping` contract.
  Each distinct condition string is compiled once per compiler invocation with
  `IFlowExpressionEngine`.
- `CompiledApplicationLink.IsMatch(...)` evaluates the cached condition.
  `TryMatch(...)` returns a condition failure for that link without requiring a
  caller to abort sibling evaluation.
- Added diagnostics for malformed declarations, invalid references, unknown
  component types, missing components or ports, missing system output metadata,
  exact payload-type mismatches, duplicate endpoint pairs, missing/invalid
  condition engines, exclusive claims, and cycles.
- Duplicate detection covers repeated declarations on one side and the same
  link declared from both endpoints. Endpoint identity is independent of the
  condition string.
- Added `CompositionPortLinkCardinality`; existing ports default to `Multiple`,
  while `Single` makes one input or output claim explicit and statically
  enforceable.
- Added `ApplicationSystemOutputMetadata` so Engine-owned system stream payload
  types participate in exact validation without adding an Engine dependency.
- Cycle detection covers self-links and strongly connected components spanning
  workflows. Cyclic runtime execution remains deferred.

## Compatibility And Versioning

- `FluxFlow.Composition` moves from local `2.0.0` to additive `2.1.0`.
- Existing legacy runtime definitions, builders, validators, and execution
  behavior are unchanged.
- The public source-declaration baseline changed only for Composition, from 210
  to 256 declarations.
- Binary package compatibility passed against the prior local Composition
  `2.0.0` artifact through an explicit temporary package source.

## Verification

- Composition tests: 116 passed.
- Composition.Hosting tests: 17 passed.
- Engine tests: 63 passed.
- Designer contract tests: 97 passed.
- Release convention tests: 93 passed.
- Complete Release solution test sweep: passed without failures or skips.
- Controlled Debug and Release builds: 0 warnings and 0 errors. Initial
  long-running invocations exceeded their command timeouts; unchanged
  incremental reruns completed successfully.
- Release preflight passed for alias `composition` version `2.1.0`.
- The first binary check requested unavailable `1.2.1`; the correct preceding
  vNext baseline is local `2.0.0`, which passed.
- A first isolated dry-run demonstrated the expected unpublished dependency
  boundary for Nodes `2.0.0`. A fresh source outside the repository was seeded
  with Data `1.0.0`, Nodes `2.0.0`, Mapping `1.0.3`, and the Composition `2.0.0`
  baseline; package archive inspection, net8 consumer build, feed verification,
  binary compatibility, and the complete dry-run then passed.

## Next Gate

Implement Engine-owned stable input mailboxes and output broadcast hubs, then
add direct `SendAsync`, `ReceiveAsync`, `ObserveAsync`, and
`SendAndReceiveAsync` behavior against canonical addresses. Reuse the compiled
link graph and preserve condition-failure isolation. Do not combine that work
with system-event/diagnostics expansion, DI revisions, component migration, or
MQTT redesign.
