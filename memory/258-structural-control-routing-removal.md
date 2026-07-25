# Structural Control And Routing Removal

Date: 2026-07-25

## Status

The obsolete structural Control and Routing compatibility surfaces are removed
on local branch `work/canonical-vnext-cleanup`. No push, tag, publication, pull
request, or merge was performed.

## Canonical Replacement Evidence

- Canonical conditioned links now have explicit tests for complementary
  true/false branches and a mutually exclusive default-route expression.
- A failed link condition is isolated from healthy siblings, preserves
  correlation, trace, and causation identity in diagnostics and system events,
  and does not prevent subsequent messages from flowing.
- Existing canonical tests continue to cover output fan-out, shared-input
  fan-in, first-fault behavior, condition compile-once behavior, and
  cross-workflow addressing.
- Links preserve payload shape. Former switch route envelopes migrate through
  an explicit mapper before conditioned links.

## Removed Control Surface

- Removed `FilterNode<T>`, `WhenNode<T>`, their options, ports, diagnostics,
  numeric errors, factories, type/port/resource constants, and Designer
  metadata.
- Removed the obsolete Control runtime and composition test projects from the
  solution after moving replacement evidence to canonical Composition and
  Engine tests.
- `FluxFlow.Components.Control` `5.0.0` and
  `FluxFlow.Components.Control.Composition` `3.0.0` remain as dependency-free,
  explicitly marked migration packages. Applications migrate definitions and
  then remove these package references.
- Release conventions now recognize `FluxFlowMigrationOnlyPackage=true` and
  verify that such composition packages expose no factories, metadata, or
  project dependencies.

## Removed Routing Surface

- Removed `FlowSwitchNode<T>`, `FlowForkNode<T>`, `FlowMergeNode<T>`, their
  options, diagnostics, structural constants, factories, dynamic-port helpers,
  metadata, and dedicated tests.
- Retained FlowValue and typed Window, Correlation, and Join contracts and
  behavior. Their focused runtime and composition tests remain in place.
- `FluxFlow.Components.Routing` moved from `4.0.0` to `5.0.0` and
  `FluxFlow.Components.Routing.Composition` moved from `2.2.0` to `3.0.0`.
- The Designer sample now registers and displays only the retained stateful
  Routing components.

## API And Package Evidence

- The reviewed source-declaration baseline now records zero Control public
  declarations and only the retained Routing declarations.
- SDK package validation against Control `4.0.0`, Control Composition `2.0.0`,
  Routing `4.0.0`, and Routing Composition `2.2.0` reported only the intended
  major-version removals. No compatibility suppressions were added.
- Release preflight and complete package dry-runs passed for all four new
  package versions using a temporary complete package source outside the
  repository.

## Verification

- Composition: 109 passed, zero warnings.
- Engine: 55 passed, zero warnings.
- Routing: 51 passed, zero warnings.
- Routing Composition: 15 passed, zero warnings.
- Designer: 112 passed, zero warnings.
- Release: 98 passed, zero warnings.
- Controlled Debug and Release builds completed 129 projects with zero errors
  and zero warnings. Cold runs exceeded their command windows; immediate
  controlled reruns supplied the authoritative successful results.
- A temporary net8.0 consumer with direct references to all 58 current package
  versions restored from the complete temporary source and built in Release
  with warnings treated as errors.

## Remaining Program Work

Continue the canonical cleanup ledger with the remaining component-family
compatibility entries, including the MQTT compatibility audit. Keep each
removal behind focused parity tests, major-version review, package dry-runs,
and complete consumer verification.
