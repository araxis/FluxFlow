# Remove manual input APIs from FlowApplication

The intended ordinary execution path reads configuration, registers factories/ports/
link delegates, builds the application and starts its sources. Sources produce messages
and connected Dataflow blocks process them. Application-level input lookup and manual
message admission are not required for this path.

Removed FlowApplication.SendAsync, FlowExtensions.GetInput and FlowWorkflow.GetInput.
No public manual-ingress facade or replacement extension was introduced. This also
removes the associated per-send lifecycle checks and linked cancellation allocation;
native block operations retain their native Dataflow behavior. Lifecycle startup,
source stop, fault propagation, coordinated drain and disposal remain.

Direct FlowOutputEndpoint.LinkTo/OtherwiseTo and endpoint To/From extensions depended
on looking up the target through the application. They are removed too. The existing
ports.Link/LinkFallback helpers resolve the actual ITargetBlock from the Build-only
keyed provider and pass it to the internal route declaration method. That method
checks membership/reference identity against workflow registrations; it does not
resolve or substitute a target via the application. Policies, predicates, fallback,
cross-application validation and route ownership are unchanged.

## Current usage

```csharp
// Component types register factories and native port selectors separately.
await services.AddFluxFlowApplicationAsync(configuration);
await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
// Configured sources now drive the connected blocks.
```

Explicit LinkFactory declarations still use:

```csharp
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.source.Output", "other.sink.Input"));
```

This is a breaking development-preview API removal. Current guides and consumers are
migrated; older dated reports remain historical. FlowOutputEndpoint state, GetOutput
and GetComponent remain for a separate review. The distinct revision-host ApplicationPorts
surface is unchanged; this step does not claim its removal or change transactional ingress.

## Verification

- Release solution build: zero warnings/errors.
- Full suite: 2,545 passed, zero failed/skipped, 29 TRX reports. One added architectural
  case on net8/net10 checks that the removed APIs are absent; existing configured
  scheduler/source, routing, lifecycle, rollback and drain scenarios remain.
- Old test injections now use native Dataflow SendAsync. Existing scenario fixtures
  have explicitly test-only lookup/link helpers that use the same keyed link provider;
  no such helpers are shipped. Routing tests gained friend access for shared fixtures.
- Tests for rejection after native input completion assert Dataflow's false result,
  not the removed application's InvalidOperationException. The obsolete pre-start
  manual-admission assertion was removed; fault/start/mutation assertions remain.
- Benchmark setup captures native input blocks before measurement. These workloads
  no longer measure application manual admission; no new performance results are claimed.
- An initial focused run encountered a retry timing timeout on net8; the final full
  suite passed that test. Initial evidence remains in core-tests.log.
- Fresh package-only Core, Routing and Configuration/Scheduler consumers run on net8/
  net10; exact versions and outcomes are recorded in verification.json. The scheduler
  consumer verifies configuration-driven production without application manual sends.

Evidence: artifacts/application-input-api-removal/. Source API baseline reviewed and
updated. No commit, push, publication or external project edits.
