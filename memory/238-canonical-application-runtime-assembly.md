# Canonical Application Runtime Assembly

Date: 2026-07-20

## Status

The canonical runtime assembly milestone is implemented locally on branch
`work/application-runtime-assembly`. No push, tag, publication, pull request,
or merge was performed.

## Runtime Boundary

- `FluxFlow.Composition` keeps ownership of the canonical flat application
  model, addresses, explicit node registrations, typed port declarations, link
  compilation, and revision planning.
- `FluxFlow.Composition.Hosting` remains Engine-independent and owns definition
  loading plus serialized revision coordination.
- `FluxFlow.Engine.Hosting` now provides the concrete
  `ApplicationRuntimeAssembler` candidate factory and stable direct-port access.
- Component and resource discovery stays explicit. Node contributors populate
  `CompositionNodeRegistry`; service contributors map the complete definition
  into a candidate-owned `IServiceCollection`. There is no assembly scanning,
  reflection activation, provider merging, or arbitrary provider fallback.

## Assembly And Ownership

- Each candidate creates one resource-revision provider and one
  workflow-revision provider per workflow.
- Factories resolve canonical resource address strings through keyed DI and
  return `ComposedNode` descriptors that are checked against their explicit
  port registrations.
- Generic port metadata carries reflection-free type dispatch so Engine can
  register stable ports and keyed Dataflow views with the exact message type.
- Canonical links compile before resource or component activation. One prepared
  port revision stages all component inputs, outputs, and the complete route
  snapshot.
- Preparation failure disposes every partial descriptor, provider snapshot,
  revision attachment, and initial port runtime while preserving the original
  failure with any cleanup failures.

## Direct Access And Revision Rule

- `IApplicationRuntimeAccess.GetRequiredPorts()` exposes the host-lifetime
  `ApplicationPortRuntime` after first activation.
- Direct send, receive, observe, and request/reply use canonical addresses and
  the same stable ports as workflow routing. Output observation remains
  broadcast and does not steal workflow delivery.
- The first active definition fixes the address, direction, port kind, and
  payload type surface. Later revisions may replace resources, components,
  attachments, and compiled links while retaining that surface.
- A surface-changing revision is rejected during preparation and the active
  revision remains available. Dynamic port-surface replacement remains a
  separate application-runtime lifecycle capability.

## Versioning

- `FluxFlow.Composition` moved from `2.4.0` to additive `2.5.0` for typed port
  metadata visitation.
- `FluxFlow.Engine` moved from `2.4.0` to additive `2.5.0` for canonical runtime
  assembly and direct hosted access.
- `FluxFlow.Composition.Hosting` did not change because its candidate and
  revision contracts were already sufficient.

## Verification

- Composition focused tests: 128 passed.
- Engine focused tests: 101 passed, including canonical JSON assembly, initial
  revision-event delivery through a workflow link, keyed
  resource replacement/disposal, linked output delivery, stable direct access,
  transactional revision replacement, descriptor rejection cleanup, and
  surface-change rejection.
- Composition.Hosting tests: 45 passed. Release contract tests: 94 passed.
- The generated public source-declaration baseline was accepted only for the
  reviewed Composition and Engine additions.
- Controlled Debug and Release solution builds completed across 130 projects
  with zero warnings and errors. The first cold Release pass reported one
  transient warning without details; the immediate warnings-only controlled
  rerun completed with zero warnings.
- `eng/list-package-releases.ps1` resolved all 58 packages and confirmed
  Composition `2.5.0` plus Engine `2.5.0` as the changed aliases.
- Release preflight and package-only `net8.0` dry-runs passed for both packages
  against a fresh external source containing all 58 current Release packages.
- SDK package validation passed for Composition and Engine against exact local
  `2.4.0` packages built from verified commit `c48b48f4` in a temporary detached
  worktree. The worktree was removed after validation.
- Graphify refresh and final repository checks passed; generated graph and
  package outputs remain ignored.
