# Version 1 Readiness Plan

Date: 2026-06-02

## Decision

Move from expansion mode into stabilization mode.

The base engine extraction is complete, the first consumer migration has proved
the boundary, and component packages now cover the common reusable workflow
building blocks. The next phase is to make the public engine surface boring,
documented, and stable.

## Result

- Main branch CI is green at commit `7c5e4a9`.
- `FluxFlow.Engine` is published at `1.0.0`.
- The first consumer migrated to the beta engine successfully before the stable
  release.
- Rebuilt component packages have been published against the `1.0.0` engine
  boundary.
- A fresh public-feed restore/build smoke passed for `FluxFlow.Engine` `1.0.0`
  plus all rebuilt component packages.
- All component packages have now completed their own `1.0.0` release track.
- Independent package releases are working.
- Public docs exist for getting started, definitions, node authoring, package
  authoring, hosting, validation, runtime states, JSON conversion, expression
  mapping, package versioning, composition, and storage adapters.
- The old location-based storage adapter package id has been removed from
  public discovery.

## Stabilization Outcome

The engine v1 readiness pass is complete.

The following freeze rules remain useful when preparing the next stable
component line:

- Do not add new component package families.
- Do not add speculative node features.
- Do not change public names casually.
- Allow bug fixes, test hardening, docs cleanup, package maintenance, and
  consumer-driven API fixes.
- Existing component packages may receive narrow patches when they unblock the
  first consumer or expose a v1 engine issue.

This freeze is about focus, not abandonment. Component packages now have their
own stable `1.0.0` line and continue to release independently from the engine.

## Version 1 Scope

`FluxFlow.Engine` can reach `1.0.0` independently from the component packages.

Engine v1 should guarantee:

- stable definition records and JSON shape
- stable runtime build/start/stop/dispose lifecycle
- stable typed port and node factory model
- stable reliable fan-out and conditional link behavior
- stable runtime state, event, diagnostic, and validation error contracts
- stable package registration helper model
- stable host lifecycle wrapper
- stable package versioning and compatibility guidance

Component packages reached `1.0.0` after their own wave-based readiness gates.
Future component package changes follow package-local compatibility rules.

## Readiness Tracks

### 1. Engine Public API Audit

Review every public type in:

- `FluxFlow.Engine.Definitions`
- `FluxFlow.Engine.Runtime`
- `FluxFlow.Engine.Components`
- `FluxFlow.Engine.Mapping`
- root hosting namespace

Questions to answer:

- Are names stable enough for long-term consumers?
- Are any public types accidentally exposed?
- Are records using the right required/optional fields?
- Are default values compatible with future versions?
- Are error codes complete enough to avoid later breaking changes?
- Should built-in expression engine implementations stay in the engine package,
  or should they be moved behind optional packages before v1?
- Is `FlowNodeId` in the right namespace and visibility shape?
- Are host-level and runtime-level names clear enough when used together?

### 2. Engine Behavior Gates

Before beta:

- full solution build
- full solution tests
- sample app run
- package pack check
- local package install smoke test
- link/fan-out regression review
- lifecycle start/stop/dispose regression review
- diagnostics/event channel regression review
- validation/build error regression review

Before v1:

- repeat the full beta gates
- run the first consumer against the beta package
- collect and resolve consumer feedback
- confirm no public API rename is still expected

### 3. Documentation Gates

Before beta:

- README quick-start compiles against the current package.
- docs match the current package list.
- docs explain engine vs component package ownership clearly.
- package versioning docs explain that engine and component packages release
  independently.
- storage docs use backend-based adapter naming.

Before v1:

- add a compact public API overview.
- add compatibility guidance for engine minor and patch releases.
- add a migration guide from `0.5.0-alpha.1` to the beta if public names change.

### 4. Component Package Audit

Focus only on packages the first consumer uses or is about to use.

For each package:

- check package id, version, README, changelog, and tag prefix.
- verify no consumer workspace schema leaked into package contracts.
- verify request/options/result contracts are package-owned and neutral.
- verify per-message failures emit structured errors when continuation is
  expected.
- verify bounded capacity and completion behavior.
- verify diagnostics are useful but not app-specific.

The component package `1.0.0` line has now passed its release gates. Future
component audits should focus on compatibility, consumer feedback, and targeted
maintenance.

### 5. Release Path

Completed sequence:

1. Complete engine API audit.
2. Fix API/docs issues found by the audit.
3. Release `FluxFlow.Engine 0.6.0-beta.1`.
4. Move the first consumer to the beta package.
5. Resolve the real package-set compatibility issue by rebuilding component
   packages against the stable engine boundary.
6. Release `FluxFlow.Engine 1.0.0`.
7. Publish rebuilt component packages.
8. Verify public restore/build from a clean package cache.
9. Complete component package `1.0.0` readiness and release all component
   packages.
10. Verify stable component package tags, release records, public feed
   visibility, release preflight, full Release build, and full Release tests.

## Current Risk Assessment

Engine v1 is complete.

Component package v1 is complete.

Main risks:

- consumer feedback may still expose targeted package fixes
- future stable component lines still need their own compatibility gates
- new component families should not disturb the stable engine boundary

## Immediate Next Work

1. Let consumers run against `FluxFlow.Engine` `1.0.0` and the stable component
   packages.
2. Handle post-1.0 feedback as targeted package fixes or additive minors.
3. Resume the backlog from the next component package or hardening item, without
   changing the stable engine boundary casually.
