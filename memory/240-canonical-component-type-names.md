# Canonical Component Type Names

Date: 2026-07-22

## Status

Canonical component and MQTT retry-resource type names are implemented locally
on branch `work/canonical-type-names`. Existing definitions remain loadable
through explicit compatibility aliases. No push, tag, publication, pull
request, or merge was performed.

## Naming Contract

- Component `Type` values use lowercase `domain.operation` names with a
  singular domain and a direct operation verb.
- Resource `Type` values use lowercase `domain.kind` names because resources
  describe reusable configuration or host-owned state.
- `Type` selects a registered factory. Processing limits, ordering, retry,
  resource references, and lifetime behavior remain ordinary configuration
  properties rather than alternate type names.
- `docs/21-component-type-names.md` is the current catalog and configuration
  boundary reference.

## Canonical Renames

| Previous name | Canonical name |
|---------------|----------------|
| `flow.mapper` | `data.map` |
| `flow.assert` | `data.assert` |
| `json.schema-validator` | `json.validate` |
| `state.reducer` | `state.reduce` |
| `event.expectation` | `event.expect` |
| `event.projection` | `event.project` |
| `metrics.aggregate` | `metric.aggregate` |
| `flow.counter` | `metric.count` |
| `flow.logger` | `log.write` |
| `flow.metrics` | `metric.measure` |
| `flow.correlation` | `flow.correlate` |
| `source.generated` | `source.items` |
| `directory.enumerate` | `directory.list` |
| `http.client` | `http.request` |
| `session.recorder` | `session.record` |
| `mqtt.control` | `mqtt.command` |
| `mqtt.trigger` | `mqtt.receive` |
| `resilience.retry` | `retry.policy` resource |

Unlisted component and resource names remain unchanged. Runtime diagnostics,
result kinds, error codes, public type names, and implementation class names
also remain unchanged because they are separate contracts.

## Compatibility

- `CompositionNodeRegistry` now registers and resolves explicit aliases while
  exposing only canonical registrations through `Registrations`.
- Default family registration methods add the previous type name as an alias.
  Explicit custom generic registrations do not claim the shared legacy alias.
- Runtime construction and validation resolve aliases through the registry, so
  stored definitions using previous names continue to execute.
- Designer metadata exposes a shared `aliases` attribute. The catalog resolves
  aliases to canonical metadata while `All` remains canonical-only, keeping
  old definitions renderable without duplicating palette entries.
- MQTT resource binding accepts both `retry.policy` and the previous
  `resilience.retry` value. New definitions and examples use `retry.policy`.
- Collision, missing-target, and invalid-alias cases fail deterministically.

## Packages

- `FluxFlow.Composition` moved from `2.5.0` to `2.6.0`.
- `FluxFlow.Components.Designer` moved from `2.19.0` to `2.20.0`.
- Mapping, Assertions, Validation, State, Expectations, Projections, Metrics,
  Observability, Routing, Sources, FileSystem, HTTP, Sessions, and MQTT
  Composition packages moved from `2.0.0` to `2.1.0`.
- Public declarations are additive. The accepted source-declaration baseline
  contains the alias registration and Designer alias metadata contracts.
- Affected release notes, package READMEs, top-level changelog, and canonical
  configuration documentation were updated. Runtime component package
  versions remain unchanged.

## Verification

- Composition tests: 131 passed.
- Designer tests: 108 passed.
- All 14 affected composition-package test projects passed, including focused
  alias, metadata, canonical fixture, and legacy MQTT retry-resource coverage.
- Release tests: 95 passed. The focused public API acceptance tests also
  passed.
- Controlled Debug and Release solution builds completed across 130 projects
  with zero warnings and errors.
- Binary compatibility passed for all 16 changed packages against the
  preceding available baseline. MQTT Composition used the exact local `2.0.0`
  package because the public feed still exposes an older major line.
- Release preflight passed for all 16 changed packages.
- A fresh temporary package source outside the repository was seeded with all
  58 current manifest packages. Archive validation, isolated `net8.0` consumer
  restore/build, local-feed verification, and release dry-run passed for all 16
  changed packages.
- Temporary package and consumer outputs remained outside the repository.
