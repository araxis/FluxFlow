# vNext Transactional Application Revisions

Date: 2026-07-17

## Status

The ninth bounded vNext milestone is implemented on local branch
`work/transactional-revisions-vnext`. No push, tag, publication, pull request,
or merge was performed.

This milestone adds complete-definition revision planning, atomic stable-port
routing activation, and an Engine-independent hosting coordinator. It does not
add dynamic port registration, payload-type migration, automatic mapping,
component-family migration, or the MQTT vertical slice.

## Composition Planning

- `ApplicationRevisionPlanner` compares the current and candidate complete
  canonical application definitions. Resource object property order is ignored
  while array order and values remain significant.
- Nested resource groups flatten to canonical resource addresses. The planner
  records added, updated, and removed resources and workflows, discovers
  resource references recursively, and computes transitive reverse resource
  dependents.
- Missing resources and resource dependency cycles produce deterministic
  diagnostics. Invalid candidates are rejected before any runtime candidate is
  prepared.
- A changed workflow is one replacement unit. Planning does not attempt
  component-level state migration or partial live graph mutation.
- Revision lifecycle records use stable phases and deterministic JSON. The
  event-sink abstraction remains independent of Engine and Hosting.

## Stable Port Activation

- `ApplicationPortRuntime.CreateRevision(...)` prepares replacements against
  already registered stable input/output addresses and exact payload types.
- Input dispatch can pause at a generation boundary. Output dispatch uses one
  immutable route snapshot, so a committed revision cannot expose a mixture of
  old and new links.
- Prepared output staging is bounded. Cancellation or preparation/activation
  failure preserves the old input attachment, routing snapshot, and current
  revision identity.
- Successful activation publishes the new routing snapshot and revision as one
  serialized runtime operation. A revision lease owns the replaced
  attachments and their later cleanup.
- Revision events map to the reliable `System.Events.Output` stream; they are
  not best-effort diagnostics.

## Hosting Coordination

- `ApplicationRevisionCoordinator` serializes candidate updates and always
  plans against the latest committed complete definition.
- Candidate factories prepare providers, components, and port revisions away
  from live routing. Provider snapshot metadata is copied and validated before
  activation.
- Candidate activation is the commit boundary. The coordinator swaps one
  immutable active snapshot only after activation succeeds, then drains and
  disposes the old candidate.
- Preparation, cancellation, or activation failure disposes the candidate and
  leaves the old revision active. Drain or disposal failures after commit are
  reported without rolling back the already visible revision.
- Lifecycle phases are Proposed, Accepted, Activated, Draining, Disposed, and
  Rejected. Event-sink failures are recorded in the update result instead of
  corrupting coordinator state.
- The candidate contract requires a throwing factory to clean partial
  preparation and a failed activation to leave the candidate safe to dispose.

## Compatibility And Versioning

- `FluxFlow.Composition` moves from local `2.2.0` to additive `2.3.0`.
- `FluxFlow.Engine` moves from local `2.2.0` to additive `2.3.0`.
- `FluxFlow.Composition.Hosting` moves from local `2.0.0` to additive `2.1.0`
  and remains free of an Engine dependency.
- The reviewed public source-declaration baseline changes only for those three
  packages. Existing declarations and legacy executable Composition contracts
  remain available.

## Verification

- Composition tests: 123 passed, including dependency closure, co-removal,
  missing dependency, cycle, structural comparison, whole-workflow unit, and
  deterministic revision-event coverage.
- Engine tests: 96 passed, including atomic route replacement, cancellation,
  failed prepared source rollback, bounded staging, and system-event mapping.
- Composition.Hosting tests: 38 passed, including invalid-candidate rejection,
  activation rollback, post-commit drain/disposal failures, cancellation,
  concurrent serialization, and unchanged candidates.
- Release convention tests: 93 passed with the reviewed public API baseline.
- Complete Release solution sweep: 1,943 tests passed across 63 projects with
  zero failures.
- Controlled Debug and Release builds each covered 130 projects with zero
  warnings and zero errors.
- Binary package compatibility passed for Composition `2.3.0` against `2.2.0`,
  Engine `2.3.0` against `2.2.0`, and Composition.Hosting `2.1.0` against
  `2.0.0` using a complete temporary local dependency source.
- Release preflight and isolated package dry-runs passed for all three changed
  packages, including archives, symbols, net8 restore/build, and feed-style
  checks. No tag was created.
- A package-only net8 consumer restored from the temporary source, exercised
  the planner, stable-port revision, coordinator, and revision-event sink APIs,
  and printed `TRANSACTIONAL_REVISION_API_OK`.
- `graphify update . --force` refreshed the ignored local graph to 14,585
  nodes and 28,904 edges.

## Deferred Boundaries

- Stable ports must exist before a revision and replacement payload types must
  match exactly. Dynamic port registration, type migration, and automatic
  mapper insertion require separate design work.
- Provider snapshot publication and concrete candidate construction remain
  host responsibilities behind `IApplicationRevisionCandidateFactory`.
- Component-specific state transfer is not implicit. A later component-family
  migration must define explicit compatibility and ownership contracts.

## Next Gate

Implement the MQTT vNext vertical slice as a separate bounded milestone over
the accepted data, canonical Composition, stable-port, system-signal, provider
snapshot, and transactional-revision foundations. Keep broker endpoints,
client controllers, commands/results, triggers, acknowledgements, diagnostics,
and runtime update behavior aligned with the recorded flat
`Resources`/`Workflows` model.
