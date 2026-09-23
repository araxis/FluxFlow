# Hot reload removed from the minimum framework

The user explicitly classified live reconfiguration as a future optional high-level
feature, not a Core requirement. This supersedes previous instructions to retain the
revision path. No replacement package was introduced.

## Removed

- FluxFlowApplication update facade, AddFluxFlowUpdates, ReloadAsync/ApplyAsync,
  revision planning, candidates, activation/swap/rollback and revision snapshots.
- Isolated candidate providers, borrowed-host fallback, stable revision ports,
  revision event/diagnostic streams and revision output capture.
- RevisionResourceRegistration and ApplicationResourceRegistrationContext. Resource
  contracts now share ordinary ResourceRegistration with optional Configuration setup.
- Application.DisposeAfterDrainAsync and its stop bypass; the revision event header.
- Revision-specific Engine.Tests and FluxFlow.Testing projects, their solution entries,
  revision load harness and revision messaging/topology benchmarks.

The old Runtime tree contributed 59 removed C# files and 7,059 source lines. This
count excludes removed resource-context files, test/benchmark source and edits elsewhere;
it is not a net repository line count. The solution now contains 30 projects, down from 32.

## Preserved

Ordinary configuration parsing, keyed DI composition, link compilation and conditional
links, component events, native blocks, component-owned request/reply, cancellation,
startup/shutdown, failed-build cleanup and host-owned disposal remain. Ordinary hosting
now lives in Core.Application; configuration loaders/readers live in Core.Configuration.
No production BuildServiceProvider call remains.

Ordinary hosting and configuration-loader tests were moved into Core.Tests rather than
deleted with the revision suite. Native forwarding/fanout/payload benchmarks remain.
Authored resource callbacks receive IServiceCollection, ApplicationAddress and the
individual ResourceInstanceDefinition; they run before provider construction. No revision
identity or host provider is required to register a resource factory.

Changing configuration now requires constructing a new host/provider. Core does not
promise seamless live swaps or rollback. Cancellation is still cooperative and failed
construction still requires provider disposal. Historical revision design reports are
retained as history, and older guides are marked accordingly.

## Validation

- Release solution build: zero warnings/errors.
- Public API baseline updated for the intentional preview removals and namespace moves.
- Core package built/restored in an isolated consumer and ran on .NET 8 and .NET 10.
- Configuration plus Scheduler/Source/Gate/Mapper and optional engine packages built,
  restored and ran in an isolated consumer on both targets.
- All 2,172 remaining solution test executions passed in Release, including 107 release
  checks. Evidence
  is in artifacts/cleanup-review-20260920/no-revisions-*.log.

The lower test count reflects removed feature suites, not weakened failure assertions.
There is also an API guard against reintroducing revision types and reload/update methods.
No commit or publication was performed. Pre-removal source snapshots are held only in
ignored cleanup artifacts; they are not compiled, packaged or a new runtime module.
