# vNext Composition Definition And Addressing

Date: 2026-07-17

## Status

The second bounded vNext milestone is implemented on local branch
`work/composition-definition-addressing-vnext`. No push, tag, publication,
pull request, or merge was performed.

This milestone stops at canonical persistence and address contracts. It does
not normalize links, compile conditions, bind component registrations, build a
runtime, change Engine definitions, modify Hosting lifecycle, migrate component
families, or redesign MQTT.

## Canonical Model

- Added immutable definitions under `FluxFlow.Composition.Model` for the
  application, workflows, components, resource groups, and resource instances.
- The persisted document requires exactly `Resources` and `Workflows`, with
  exact casing and no additional root properties.
- Workflow objects contain components directly. Components and resource leaves
  require `Type`; resource groups omit it and form nested namespaces.
- Component and resource settings remain direct `JsonElement` properties.
  Legacy `Configuration` and per-component `Resources` wrappers are rejected.
- Names use ordinal comparison, are not normalized, and reject empty, dotted,
  surrounding-whitespace, duplicate, and reserved forms.
- Constructors copy source collections into immutable ordinal dictionaries and
  clone retained JSON values.

## JSON And Configuration

- Added `ApplicationDefinitionJson` with a strict converter and deterministic
  writer. Root order is fixed, named maps and object properties are sorted
  ordinally, nested objects are canonicalized recursively, and array order is
  retained.
- Duplicate properties are rejected at every retained JSON object depth.
- Added `ApplicationDefinitionConfigurationLoader` for a configuration root or
  explicit host section. The strict JSON reader remains authoritative after
  provider projection.
- Extracted the existing configuration-to-JSON traversal into an internal
  shared helper without changing the legacy loader contract.
- Empty workflow and resource-group objects that configuration providers flatten
  to null markers are restored where the canonical shape makes their object
  role unambiguous.

## Address Contract

- Added `FluxFlow.Composition.Addressing.ApplicationAddress` with ordinal,
  case-sensitive equality and one parse/resolve path.
- Supports nested `Resources.Group.Resource` addresses, absolute
  `Workflow.Component.Port` addresses, and local `Component.Port` references
  resolved against a workflow.
- Reserves `System.Events.Output` and `System.Diagnostics.Output` as the only
  system addresses.
- Rejects blank or ambiguous segments, surrounding whitespace, resource paths
  used as ports, and unrecognized system paths.

## Compatibility And Versioning

- `FluxFlow.Composition` moves from local `1.2.1` to `2.0.0` because it depends
  on Nodes `2.0.0` and introduces the canonical vNext public contract.
- Existing `CompositionDefinition` runtime DTOs and fluent/config APIs remain
  available temporarily as an explicitly documented migration surface.
- Canonical link-shaped component properties are persisted but have no runtime
  meaning yet.
- The public source-declaration baseline changed only for the Composition
  package, from 155 to 210 declarations.

## Verification

- Composition tests: 101 passed.
- Composition.Hosting tests: 17 passed.
- Release convention tests: 93 passed.
- Complete Release solution test sweep: passed without failures or skips.
- Controlled Debug build: 0 warnings and 0 errors.
- Controlled Release build: 0 warnings and 0 errors.
- Binary package compatibility passed against published Composition `1.2.0`.
  The initially selected `1.2.1` is local and unpublished, so it is not an
  available package baseline.
- Release preflight passed for alias `composition` version `2.0.0`.
- A temporary source outside the repository was seeded with Data `1.0.0` and
  Nodes `2.0.0`; the Composition package archive, isolated net8 consumer build,
  and dependency-source verification all passed.

## Next Gate

Implement link parsing, direction inference, canonical normalization,
condition compilation, duplicate/exclusive-claim/type/cycle validation, and
fanout semantics as a separate bounded milestone. Reuse
`ApplicationAddress`; do not begin stable runtime ports, DI revisions,
component migration, diagnostics, or MQTT in that pass.
