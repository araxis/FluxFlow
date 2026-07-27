# Hosted Engine Simplification

Date: 2026-07-27

## Outcome

FluxFlow now has one maintained application-hosting facade and one revision
lifecycle owner. `FluxFlowApplication` in `FluxFlow.Engine` is the object that
direct callers resolve, the object driven by `IHostedService`, and the object
that owns startup, reload, direct apply, revision replacement, stop, and
disposal.

The work remains local on `work/hosted-engine-simplification`. No branch was
pushed, and no tag, package publication, pull request, or merge was created.

## Final Package Boundaries

- `FluxFlow.Composition` `5.1.0` remains the reusable graph and activation
  foundation. It now also owns the host-independent application extension
  contracts: `IApplicationResourceRegistrar`,
  `ApplicationResourceRegistrationContext`, and the keyed resource/component/
  port/signal DI helpers.
- `FluxFlow.Engine` `6.0.0` is the maintained application package. It owns
  `IApplicationDefinitionSource`, static and configuration-backed definition
  sources, `AddFluxFlow(...)`, `FluxFlowApplication`, hosted startup/stop,
  revision planning and execution, runtime assembly, stable application ports,
  and revision snapshots.
- `FluxFlow.Composition.Hosting` `6.0.0` is an obsolete compatibility package.
  Its old registration, host, options, source, and keyed-DI entry points forward
  to Engine or Composition. It contains no independent runtime coordinator.
- `FluxFlow.Components.Mqtt.Composition` `5.0.1` consumes the registrar and keyed
  DI contracts directly from Composition and no longer depends on Hosting.
- Engine references Composition, Mapping, and Nodes. Engine does not reference
  Hosting. Hosting references Composition and Engine, so the dependency graph
  is one-way and acyclic.

## Unified Application API

One `services.AddFluxFlow(...)` call registers the complete application runtime.
The overloads accept an `ApplicationDefinition`, an `IConfiguration` root or
named section, an `IApplicationDefinitionSource` instance, or a generic custom
definition source. Options cover the initial revision id, hosted start/stop, and
stable port capacities.

`FluxFlowApplication` exposes:

- `StartAsync`, `ReloadAsync`, `ApplyAsync`, `StopAsync`, and async disposal.
- `State`, `CurrentDefinition`, `Current`, and `LastUpdate`.
- `Ports` for metadata, status, system events, diagnostics, rejections, direct
  send/receive, observation, and trace-correlated request/reply.

The hosted adapter is deliberately thin. It invokes the same singleton
`FluxFlowApplication` that direct consumers resolve; it does not maintain a
second state machine or revision coordinator.

## Lifecycle And Ownership

- A single application gate serializes start, reload, apply, stop, and disposal.
- Revision application remains transactional: normalize and plan, prepare an
  isolated candidate, publish acceptance, activate, atomically swap the active
  revision, drain the previous candidate, and dispose old ownership.
- Preparation or activation failures preserve the prior active revision and are
  reported as rejected `ApplicationUpdateResult` values. Cancellation rolls
  back the uncommitted candidate.
- Resource registrars populate each revision-owned service collection while host
  services and explicitly external resources remain non-owning.
- Runtime assembly, revision candidates, revision planning details, and port
  generation builders are internal Engine machinery. Public consumers operate
  through `FluxFlowApplication`, its snapshots/results, and `ApplicationPorts`.
- Stop and disposal are idempotent at the application boundary. Cleanup still
  attempts all owned resources and reports aggregate failures.

## Compatibility

The obsolete Hosting package preserves source migration paths without preserving
a parallel architecture:

- `AddFluxFlowApplication(...)` forwards to `AddFluxFlow(...)` and adds legacy
  facade services.
- `ConfigureFluxFlowApplication(...)` maps legacy options to
  `FluxFlowApplicationOptions`.
- `IApplicationRevisionHost` and `ApplicationRevisionHost` delegate lifecycle
  and state to the Engine application.
- `AddFluxFlowEngine()` is a no-op bridge because Engine registration is already
  complete.
- Legacy keyed registration helpers forward to the Composition helpers.

The compatibility package is scheduled for removal in a later major version.
It must not receive new runtime behavior.

## Package Versions And API Review

- Composition: `5.1.0`.
- Engine: `6.0.0`.
- Composition.Hosting: `6.0.0`.
- MQTT Composition: `5.0.1`.

The reviewed public API baseline was updated for intentional moves and removals.
Composition and MQTT Composition pass SDK package validation against their
preceding releases. Engine and Hosting report only the expected `CP0001`
diagnostics for intentionally removed public runtime machinery and moved legacy
hosting contracts. No compatibility suppressions were added.

## Verification

- Characterization and focused suites passed: Composition `104`, Hosting
  compatibility `7`, Engine `84`, MQTT Composition `9`, Fluent `18`, and Fluent
  Hosting `5` tests.
- All 19 component Composition suites passed, totaling `295` tests.
- Release tests passed: `100` tests.
- Controlled Debug and Release solution builds each completed `137` projects
  with zero warnings and zero errors.
- All `62` current manifest packages packed into a fresh temporary source outside
  the repository.
- Release preflight and complete-local-source package dry-run passed for all four
  affected packages: Composition, Engine, Composition.Hosting, and MQTT
  Composition.
- A fresh external `net8.0` consumer restored only packaged FluxFlow artifacts,
  built with zero warnings, and ran successfully with
  `HOSTED_ENGINE_CONSUMER_OK`. It exercised configuration registration, hosted
  and direct lifecycle identity, application ports, a custom definition source,
  a revision resource registrar, and the obsolete Hosting forwarding facade.

## Follow-Up Boundary

Future application-host changes must extend `FluxFlowApplication` or its focused
collaborators without adding another public coordinator or lifecycle gate.
Composition.Hosting should be removed in a separately planned major release once
consumers have migrated. Observability naming and signal harmonization remain a
separate concern and should not be coupled to hosting cleanup.
