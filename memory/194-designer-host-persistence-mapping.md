# Designer Host Persistence Mapping

Date: 2026-07-03

## Summary

Implemented phase 4 of the Designer host layer plan: persistence mapping
between the host graph model and `FluxFlow.Composition` definitions in
`samples/FluxFlow.DesignerHost`, plus the shared validation message mapping.
The mapping is lossless for definition content and keeps host layout out of
definitions.

## Changes

- Added `GraphModel` / `GraphNodeModel` / `GraphLinkModel` /
  `GraphLayoutModel`: an editable workflow graph with node names, component
  types, raw `JsonElement` option values, resource references, optional
  cross-workflow link segments, and host-only canvas layout.
- Added `GraphDefinitionMapper` with `ToDefinition(graph|graphs)` and
  `FromDefinition(definition[, workflowName])`. Duplicate workflow names and
  unknown workflow lookups throw clear errors. Layout never enters a
  definition; `FromDefinition` leaves layout at its default and the host
  merges saved layout separately.
- Added `ValidationMessageMapper` mapping `DesignerMetadataValidationError`
  (Metadata source, path-prefixed message) and `CompositionDiagnostic`
  (Composition source, node context) into `ValidationMessageModel`.
- `FluxFlow.DesignerHost` now also references `FluxFlow.Composition`.
- Updated the sample README and the coverage matrix candidate note: renderer
  UI is the only remaining Designer host pass.

## Boundaries

- No component package source, versions, release notes, changelog entries,
  public API baselines, tags, or publishing state changed.
- Renderer UI, resource catalogs, keyed service registration, and resource
  lifetimes remain outside the host-model layer per the plan's non-goals.

## Verification

- Host-model tests passed: `29` passed, `0` failed, `0` skipped (9 new:
  JSON-equality definition round-trips including cross-workflow links,
  layout exclusion, duplicate/unknown workflow errors, and validation message
  mapping).
- Release convention tests and the full Release solution suite passed after
  the change.
