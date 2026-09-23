# Resource registration timing and application composition

## Corrected behavior

The ordinary BuildApplicationAsync path formerly retrieved ResourceRegistration callbacks
and supplied them with a new IServiceCollection at Build time. Configuration had deferred
registration itself, not only construction. This violated the agreed Add/Build boundary.

Configuration now invokes type-keyed ResourceRegistration callbacks during
AddFluxFlowApplicationAsync. Their arguments are registration services, canonical resource
address and immutable parsed definition. No registration context or provider is passed.
Unknown resource types fail during registration; callbacks register native factories.
The package consumer no longer needs an IApplicationResourceRegistrar implementation class.

Core AddApplicationResources runs its callback immediately, copies/freezes native service
descriptors and registers a factory for the owning resource provider. Repeated Add calls
merge definitions; failed registration leaves the previous service collection intact.
Resource creation remains deferred/lazy during construction/resolution. The callback never
runs again in Build. There is no registration-time provider construction.

FlowApplication now owns a clear internal composition sequence:

```text
AddResources -> AddComponentsAsync -> MakeLinks -> Validate
StartAsync remains a separate explicit call.
```

The BuildApplicationAsync extension creates an Application and invokes that sequence.
No Template Method inheritance, public executor or factory class was introduced. Moving
composition into a partial file is organization; the actual behavioral simplification is
the corrected registration timing and the removal of the delayed registration contract.

## Preserved boundaries and remaining machinery

The operational model remains one Application per host instance. Multiple Build tests
verify isolation and cleanup, not a new hosting concept.

ApplicationResourceScope remains internal: it builds a native provider from the frozen
registrations and borrows the original host as fallback. Owned resources are disposed
after components; borrowed host-scoped instances retain identity and are not disposed.
This internal scope/provider was not removed. The short-lived port provider also remains,
registering only already-created native instances for LinkFactory resolution. It performs
no resource configuration or component construction and owns none of those instances.

The older revision runtime still uses its IApplicationResourceRegistrar/context path.
It was not changed in this ordinary Application slice. Tests exercising its definition
registrars now explicitly invoke them at registration time for ordinary Build. Do not
claim all historical configuration infrastructure was removed.

The old Core ResourceRegistration(resources, host) API is a breaking removal. Registration
callbacks must move binding/definition work to Add and creation to the registered factories;
there is no host IServiceProvider available during registration. Host resource fallback
for component factories and application cleanup ordering remain verified.

## Verification

Release build: zero warnings/errors. Full suite: 2,643 passed, zero failed/skipped,
29 TRX reports. API source baseline reviewed and accepted.
New/updated tests explicitly check registration occurs at Add, is not rerun during Build,
frozen descriptors reject late mutation, repeated registration merges, failed registration
does not corrupt prior registrations, resources remain independent across Builds, host
scoped dependencies retain identity, and node cleanup precedes resource cleanup.

Fresh configuration package consumer exercises Scheduler -> Source -> Gate -> JSONata
Mapper -> C# Mapper -> async sink plus resource construction/cleanup and asserts resource
registration count remains one after Build. Evidence is in
artifacts/resource-registration-stage/ (build-final.log, tests/, tests.log, package.log).
Both net8/net10 passed with eight candidate packages version 0.0.0-local.20260920062113.
No commit, push, publication or external-project changes.
