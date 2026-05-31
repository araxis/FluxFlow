# FluxMq Migration Spike Review

Date: 2026-05-31

## Scope

Reviewed `memory/report.md` against the current `FluxFlow.Engine` source and the
current FluxMq codebase in `D:\Projects\FluxMq`.

## Verdict

The report is directionally correct: FluxMq needs a workspace document that owns
dashboards and tests, while FluxFlow should own only executable workflow engine
structure and runtime behavior.

The main adjustment is sequencing. Removing scenario/testing APIs from
`FluxFlow.Engine` is the right boundary target, but it should be done as a
deliberate package-boundary change, not as a casual migration cleanup. FluxMq
still has real CLI, UI, and app code around scenarios, so deleting those pieces
without a replacement layer would create avoidable migration churn.

## Correct Findings

- `FluxMqApplicationDefinition` should exist in FluxMq, not FluxFlow.
- `FluxMqApplicationDefinition.ToEngineDefinition()` should project only
  `resources` and `workflows` into `FluxFlow.Engine.ApplicationDefinition`.
- Dashboard layout, widget, filter, and validation concepts belong in FluxMq.
- MQTT node types, event type constants, scenario step types, and scenario
  runners belong in FluxMq.
- Runtime diagnostics, lifecycle state, event collection, node address mapping,
  and node metadata should stay in FluxFlow.
- Generic mapping contracts and expression engines can stay in FluxFlow while
  they remain protocol-neutral.
- Resources are still valid in FluxFlow when treated as shared runtime-scoped
  nodes rather than as domain-specific connections.

## Stale Or Incomplete Parts

- Dashboard removal is already complete in FluxFlow. The engine JSON options no
  longer contain dashboard converters, and unknown dashboard metadata is ignored.
- `ApplicationDefinition.Tests` and scenario runner APIs still exist in
  FluxFlow. That is the remaining boundary overlap called out by the report.
- The report treats scenario removal as a prerequisite for migration. A safer
  migration can start first by projecting FluxMq workspace documents into engine
  definitions and never passing `tests` to FluxFlow.
- The report does not mention that removing `Tests`, `ScenarioDefinition`, or
  host scenario APIs is a public package break after `0.1.0-alpha.1`.
- The report does not call out the current host diagnostics stream. A FluxMq host
  facade should preserve access to runtime diagnostics when wrapping the engine
  host.

## Recommended Decision

Keep the report's target architecture, but split it into two tracks:

1. Short-term FluxMq migration:
   - Add a FluxMq-owned workspace definition with `resources`, `workflows`,
     `dashboards`, and `tests`.
   - Project only `resources` and `workflows` into FluxFlow.
   - Do not use the FluxFlow configuration loader directly for FluxMq workspace
     files.
   - Keep FluxMq scenario execution in FluxMq while replacing the runtime graph
     with FluxFlow.

2. Next FluxFlow package boundary:
   - Move scenario definitions, runner, event journal, and step registry into a
     separate testing layer/package or a FluxMq-owned layer.
   - Remove `ApplicationDefinition.Tests` from FluxFlow.
   - Remove scenario validation from `ApplicationDefinitionValidator`.
   - Remove `RunScenarioAsync` and default scenario runner factories from
     `FlowApplicationHost`.
   - Update README/package metadata after the move so the engine package no
     longer advertises scenario testing.

## Migration Order

1. In FluxMq, introduce `FluxMqApplicationDefinition`.
2. Move or keep dashboard and test JSON converters in FluxMq.
3. Add `ToEngineDefinition()` and tests proving dashboards/tests are excluded.
4. Update FluxMq app/CLI/UI loaders to load the workspace definition, then build
   FluxFlow from the projected engine definition.
5. Migrate FluxMq components to FluxFlow node contracts and helper base classes.
6. Replace `FluxMq.Pipeline` references slice by slice.
7. Move FluxMq-specific scenario runners and constants out of the old pipeline
   project before deleting it.
8. Remove `FluxMq.Pipeline` only after equivalent FluxFlow and FluxMq test
   coverage is passing.

## Main Risk

If FluxMq passes its full workspace JSON directly into `FluxFlow.Engine`, tests
with FluxMq-specific scenario step types can fail engine validation because the
current engine only knows its built-in generic scenario step types. The
workspace projection avoids this and should be the first migration guardrail.
