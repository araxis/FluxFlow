# Ordinary applications use one construction path

The contextual direct-application builder has been removed, following removal of
the portable ComponentBindings path. This is a breaking preview API cleanup.

## What changed

- Deleted ApplicationConstructionExtensions: no independent context activation,
  linking, validation or rollback loop for ordinary applications.
- Deleted ComponentGraphView. FlowWorkflow stores the actual IFlowNode once;
  application lookup, port control identity and lifetime use that same instance.
- Kept ComponentFactory(IServiceProvider) synchronous. Build resolves factories,
  adds native port selectors, invokes LinkFactory and validates the result.
  Runtime FlowApplication does not receive a service provider.
- Added ResourceRegistration(IServiceCollection, IServiceProvider) for existing
  application-owned resource behavior. It registers descriptors only. Build uses
  the existing ApplicationResourceScope, with fallback to borrowed host services.
  Resources are disposed after components, including failed construction/startup.
- Configuration now records resource registration for root/workflow declarations;
  neither reading configuration nor constructing the host provider creates them.
  Repeated Builds get independent resources. GetRequiredFlowResource resolves
  relative/full addresses and explicit host keys without an activation context.
- Named event selectors use the existing ComponentActivityBridge to preserve
  source/workflow/component headers and routable event outputs. This remains a
  composition concern, outside business components. No new adapter class was added.
- Application observes native input failures alongside node completion, so extra
  component completion work does not conceal a block fault.

The path is now configuration -> DI registrations -> BuildApplicationAsync ->
FlowApplication lifecycle. Components retain their own Completion and disposal
responsibilities. Asynchronous source initialization belongs to source StartAsync;
there is no shim that invokes asynchronous contextual factories in ordinary Build.

This does not merely rename or relocate the old construction loop. Its file and
adapter are deleted, and its application behavior tests now invoke the actual DI
builder. Existing resource registration and event stamping code is reused.

## Validation

- Release solution build: zero warnings and errors.
- Full suite: 2,523 passed, zero failed, 29 TRX reports across configured targets.
  The previous count was 2,533: redundant cases for removed overloads/entry paths
  were consolidated; counts are not a measure of behavior coverage.
- Migrated coverage includes local/shared/cross-workflow resources, host-scope
  identity, actual node identity, renamed controls, accepted-work drain, cancellation,
  original and cleanup failures, duplicate-node rejection and named event delivery.
- Existing document tests cover native predicates, conditional links, fallback,
  invalid ports, cycle rejection and cleanup through DI construction.
- Revision lifecycle parity still passes alongside direct application tests.
  Contextual factory tests retained for revision contracts invoke that binder itself,
  rather than recreating a deleted application builder in test code.
- API baseline updated; reflection regression checks both deleted types are absent.
- Fresh package-only Core, Routing and Configuration consumers passed on net8/net10.
  Configuration's consumer also checks deferred resource creation and cleanup order.
- Evidence: artifacts/context-construction-removal/ (build.log, api.log, tests.log,
  tests/, core-package.log, routing-package.log, configuration-package.log,
  corresponding isolated consumer directories and verification.json).

## Remaining boundary

ComponentActivationContext, ComponentInstance and their registrations still support
revision activation, resource snapshots and revision replacement. They are not used
by ordinary application Build, but they are not deleted from Core. The next review
must examine that remaining boundary and preserve revision-specific lifecycle and
rollback before removing those contracts. Link-provider/endpoint redesign is also
not part of this step. No new component setup or external telemetry was added.

Changes remain local; no commit, push or publication was performed.
