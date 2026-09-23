# Construction convergence: first path removed

The user prioritized eliminating parallel construction mechanisms, preserving
behavior, in short reviewable steps. Inspection found three construction entry
paths: the current DI builder, portable ComponentBindings, and the older
context/resource-based ApplicationConstructionExtensions. Revision hosting also
continues to use the older activation contracts.

## Completed

Removed the portable ComponentBindings path from production:

- Deleted ComponentBindings (its private type map and constructor/port callbacks).
- Removed both FlowJson.BuildAsync overloads and their independent construction
  and cleanup loop.
- Deleted PortableApplicationExtensions and its binding-based build overloads.
- Kept document serialization/parsing and linking. FlowJson.ApplyLinks now also
  accepts ApplicationSetup, applying portable links and native C# predicates to
  already-registered keyed ports. It never creates or disposes components.

The current path remains:

1. Configuration registration records ComponentFactory and native port selectors
   in IServiceCollection, without constructing nodes.
2. IServiceProvider.BuildApplicationAsync creates the workflows/nodes, registers
   their native ports, invokes LinkFactory delegates and validates the result.
3. FlowApplication starts, drains and disposes the successfully built application.

Existing document tests now register factories/port selectors and call the actual
production DI builder. DocumentApplicationFixture is test-only setup for existing
document scenarios; it contains no component construction or rollback loop and is
not a new public configuration API. The optional Configuration package retains its
section-based ComponentRegistration contract. Core and Routing package consumers
were migrated without adding new package dependencies.

This is a breaking development-preview API change. Consumers of the deleted APIs
must migrate; there is no forwarding compatibility shim. Ordinary node processing,
flat link grammar and JSON representation are unchanged.

## Validation

- Release solution build: zero warnings/errors.
- Full solution: 2,533 passed, zero failed, 29 TRX reports on the configured targets.
- Existing cases cover conditional/native links, cross-workflow fanout, fallback,
  rejection policies, invalid-link/cycle cleanup, source lifetime and host ownership.
- Resource rejection tests moved to the actual Configuration registration package;
  both root and workflow resource declarations fail before registration begins.
- API baseline reviewed/updated; a regression asserts the deleted public types
  and FlowJson construction methods remain absent.
- Fresh package-only Core and Routing consumers passed on net8.0 and net10.0.

Evidence: `artifacts/single-construction/` (build.log, api.log, focused.log,
tests.log, tests/, verification.json, package-core.log, package-routing.log,
core-consumer/, routing-consumer/). Focused tests overlap the full total.

## Still pending — do not report complete convergence

ApplicationConstructionExtensions still constructs applications through
ComponentActivationContext and ComponentInstance/ComponentGraphView. It supports
behavior not currently offered by section-based configuration:

- Application/workflow resource activation and disposal, with host dependencies
  retaining host ownership.
- Asynchronous activation, explicit completion/extra cleanup and event bindings.
- Legacy port metadata/cardinality and processing-profile validation.

The revision runtime also consumes these contracts. The next step is to transfer
the required behavior to the intended contracts, then remove the contextual
construction path. Do not delete those APIs solely because the new Scheduler
configuration path passes. Do not fold their responsibilities into FlowApplication
or extend ComponentFactory to receive Application/Workflow/activation contexts.

The Build-only link provider and endpoint representations were not redesigned in
this step. No commit, push, merge or publication was performed.
