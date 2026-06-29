# Composition And Designer Progress

Date: 2026-06-29

## Current Direction

FluxFlow is now following the standalone-node-first architecture:

- `FluxFlow.Nodes` and pure component runtime packages define standalone nodes.
- `FluxFlow.Composition` is the default optional composition layer for fluent
  and configuration-driven standalone workflows.
- `FluxFlow.Composition.Hosting` is the optional host bridge for keyed resource
  resolution and hosted runtime lifecycle.
- Component `.Composition` packages own node registration and optional Designer
  metadata for their component family.
- `FluxFlow.Engine` remains optional advanced runtime infrastructure, not the
  required composition path.
- Host or backend adapters own concrete clients, stores, credentials, policies,
  and lifetimes. Composition packages consume those services through named
  resources.

## Implemented Since The First Composition Adapter

The composition adapter sweep has moved beyond the initial MQTT package. The
normal standalone component families now have optional `.Composition` packages
with explicit factory registration, typed ports, resource names, focused tests,
package wiring, and README guidance:

- HTTP
- Mapping
- Control
- Assertions
- Validation
- Timers
- Sources
- Routing
- Serialization
- Payloads
- Observability
- Projections
- Metrics
- Expectations
- FileSystem
- State
- Storage
- Sessions
- MQTT

Request/reply was intentionally skipped as a normal component-family adapter.
Journal remains support-only because it exposes store/support contracts rather
than a normal composition node surface.

The adapter model stayed consistent:

- Closed generic registrations define CLR payload types where needed.
- Dynamic behavior, such as routing ports, is resolved at factory/build time.
- Host-owned resources are resolved by named keyed DI entries.
- Component runtime packages remain free of composition and engine dependencies.
- No hot reload or renderer behavior was added as part of the adapter passes.

## Designer Boundary

`FluxFlow.Components.Designer` was decoupled from engine identifiers and now
owns its own design-time value types. The Designer package is composition and
engine neutral.

Important follow-up work already exists:

- Package-owned Designer metadata providers were added across composition
  packages.
- Shared metadata builder/helper APIs were introduced to reduce provider
  duplication.
- Provider validation and release convention coverage were strengthened.
- Resource metadata helpers and picker hints were added while keeping actual
  resource ownership in the host.
- Option hint helpers were added in Designer:
  `OptionDesignMetadataAttributeNames`,
  `OptionDesignMetadataAttributeValues`, and
  `OptionDesignMetadataAttributes`.

## Latest Local Work

The latest local branch is `work/designer-value-type-hint-contract`.

Latest local commits:

- `95384c9 Add mapping designer metadata hints`
  - Added first-class option hint helpers in Designer.
  - Enriched Mapping composition metadata with option sections, importance,
    editor/syntax hints, related resource hints, and resource key patterns.
  - Bumped Designer to `2.16.0` and Mapping Composition to `1.3.0`.
- `abba963 Add control designer metadata hints`
  - Applied the same option/resource hint pattern to Control metadata for
    filter and when nodes.
  - Bumped Control Composition to `1.3.0`.
- `Add assertions designer metadata hints`
  - Applied the option/resource hint pattern to Assertions metadata for the
    assertion node.
  - Bumped Assertions Composition to `1.3.0`.
- `Add state designer metadata hints`
  - Applied the option/resource hint pattern to State metadata for the reducer
    node.
  - Bumped State Composition to `1.3.0`.

## Verification Notes

Recent focused verification passed for the Mapping, Control, Assertions, and
State hint passes:

- Designer tests.
- Mapping composition tests.
- Control composition tests.
- Assertions composition tests.
- State composition tests.
- Release convention tests with public API baselines updated where intended.
- Full solution build using the reliable controlled command:

```powershell
dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly
```

Build verification can appear unreliable when stale local build parent
processes survive timed-out verification attempts. The safe recovery pattern is
to stop only FluxFlow-owned stale build parents, run
`dotnet build-server shutdown`, and rerun the controlled command above. Do not
stop unrelated `dotnet` processes from other workspaces.

Local graph output was refreshed after the State hint pass closeout. The local
HTML graph was skipped because the graph exceeds the visualization size limit.

## Current Constraints

- Keep passes narrow and locally committed.
- Do not push, open PRs, or merge unless explicitly requested.
- Do not restart a broad component-family implementation sweep without a
  bounded plan.
- Do not add engine dependencies to pure component packages or `.Composition`
  packages.
- Do not move concrete resource/client/store ownership into composition.
- Do not add renderer behavior, resource pickers, hot reload, or runtime
  lifecycle extensions during metadata hint passes.
- Keep project-visible names and user-facing text neutral.

## Suggested Next Pass

Continue the richer Designer metadata hint rollout one package family at a
time. Observability is the next reasonable candidate because it already has
expression-related counter options and host-owned selector, context factory,
and clock resources, but the next pass should be planned separately.
