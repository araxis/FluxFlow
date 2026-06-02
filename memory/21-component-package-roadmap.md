# Component Package Roadmap

Date: 2026-05-31

## Goal

Keep `FluxFlow.Engine` small and protocol-neutral while allowing reusable
component families to ship as separate packages.

The engine package should not absorb MQTT publishers, MQTT triggers, HTTP
clients, file writers, replay sources, or app-specific scenario concepts. Those
belong in opt-in packages or in the consuming application.

## Package Model

Each component package should own:

- Its own source project in the solution.
- Its own package identity and package metadata.
- Node type constants or descriptors.
- Component implementation classes.
- Option models and option parsing helpers.
- Request, result, and output records for package-owned ports.
- Validation for package-specific requirements.
- Runtime factory registration.
- Package-specific diagnostic and event names.
- Focused tests and samples.
- Public README content for that component family.

The consuming application chooses which packages to reference and register.
This keeps each application free to define its own workspace JSON, section
names, and UI metadata while projecting the executable part into
`ApplicationDefinition`.

Package contracts should be designed from the component category outward. A
consumer application can prove the boundary, but it must not leak its workspace
schema, naming, storage, dashboards, or scenario concepts into reusable package
contracts.

One component family maps to one source project and one package artifact. The
repository can build all projects together, while each release run publishes one
selected package.

## Candidate Packages

Start with one package as the template before splitting everything:

- `FluxFlow.Components.Mqtt`: MQTT connection, trigger, publisher, metrics,
  router, filters, and MQTT-specific mapping helpers.
- `FluxFlow.Components.Files`: file writer and file-mapping helpers.
- `FluxFlow.Components.Replay`: replay source, recorder, and recorded-message
  mapping helpers.
- `FluxFlow.Components.Validation`: JSON schema validation and assertion
  helpers that are not tied to one application.
- `FluxFlow.Components.Assertions`: expression-driven assertion checks with
  result and pass/fail routing.
- `FluxFlow.Components.Sources`: deterministic generated and sequence source
  nodes without transport or app storage dependencies.
- `FluxFlow.Components.Routing`: expression-driven switch routing, followed by
  correlation, window, join, fork, and merge nodes after the route contract
  settles.
- `FluxFlow.Components.Observability`: neutral logger, metrics, and counter
  observer nodes.
- `FluxFlow.Components.Timers`: neutral interval and scheduling source nodes.
- `FluxFlow.Components.Storage`: host-adapter-backed logical record storage
  nodes for put, get, query, and delete flows.

The first consumer can keep components in its own repository until the package
boundary is stable. The first extracted package should prove the pattern before
other families follow.

The expanded category catalog and reusable component template are recorded in
`31-component-catalog-and-template.md`.

## Registration Convention

The desired authoring shape is:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents(options => options
        .UseClientFactory(profile => new MqttBrokerClient(profile)));
```

The exact names can change, but the convention should stay explicit:

- No assembly scanning.
- No hidden global state.
- Dependencies are passed through options or delegates.
- Registration returns the same `RuntimeNodeFactoryRegistry` for composition.
- Components expose normal engine ports, diagnostics, and events.
- Components use `Input` as the default inbound port and put semantic meaning
  in typed request records such as `MqttPublishRequest`.

## Boundaries

Keep out of reusable component packages unless a real second consumer needs it:

- application workspace schemas.
- application dashboards.
- application scenario definitions and scenario step runners.
- application storage repositories.
- application UI models.

If scenario support becomes reusable later, consider a separate testing package
after the core component package has settled.

## Sequence

1. Record the first consumer migration result. Done.
2. Add a small neutral consumer sample in FluxFlow. Done.
3. Introduce a package registration helper pattern in the engine if needed. Done
   with `IFlowNodeModule`, `FlowNodeModule`, and `FlowNodeRegistration`.
4. Extract one package family, preferably MQTT, only after the first consumer's
   feature work settles. Done with adapter-backed publish and subscribe nodes.
5. Publish the package as a prerelease and migrate the first consumer from
   local components to that package in a small follow-up branch.
6. Add generic mapping, control, validation, file system, and observability
   packages as independent package artifacts. Done through the first
   observability package release.
7. Add a generic timer package for interval-driven workflow activity. Done with
   `timer.interval`.
8. Add a generic storage package for host-adapter-backed put/get/delete
   workflow storage. Done in `63-storage-component-package.md`.
9. Plan a neutral persisted storage adapter package and host migration path.
   Done in `65-storage-adapter-and-migration-plan.md`.
10. Add the first file-system-backed storage adapter package. Done in
    `66-storage-filesystem-adapter-package.md`.
11. Split expression-driven assertions out of the control package. Done in
    `67-assertions-component-package.md`.
12. Add deterministic generated and sequence sources. Done in
    `68-sources-component-package.md`.
13. Add first expression-driven routing package. Done in
    `69-routing-component-package.md`.
14. Add key-based request/response correlation to routing. Done in
    `70-routing-correlation-component.md`.
15. Add direct route-specific output ports to `flow.switch`. Done in
    `71-routing-switch-output-ports.md`.
16. Add count/time stream windows to routing. Done in
    `72-routing-window-component.md`.
17. Add two-stream key joins to routing. Done in
    `73-routing-join-component.md`.
18. Add same-stream fork, source-tagged merge, and optional switch route
    envelopes. Done in `74-routing-merge-fork-route-envelope.md`.
19. Add logical storage query and file-system adapter query support. Done in
    `75-storage-query-component.md`.
20. Record the storage adapter package rule: base storage stays logical, and
    each reusable persistence implementation lives in its own
    `FluxFlow.Components.Storage.*` package. Done in
    `76-storage-adapter-package-rule.md`.
21. Refine storage adapter naming so each adapter package maps to exactly one
    concrete backend, not a broad persistence family. Done in
    `77-storage-adapter-backend-naming.md`.
22. Rename the first persisted storage adapter to a backend-based
    file-system package and API. Done in
    `78-storage-filesystem-adapter-rename.md`.
23. Add shared component expression registration helpers and migrate Mapping,
    Control, Assertions, State, Observability, and Routing to use them while
    preserving each package's public registration API. Done in
    `89-shared-expression-support.md` through
    `95-expression-support-migration-complete.md`.
24. Harden deterministic Sources behavior with a host-provided source clock for
    delay scheduling and sequence timestamps. Done in
    `98-sources-clock-hardening.md`.
25. Harden deterministic Sessions behavior with a host-provided session clock
    for recorder timestamps and replay delays. Done in
    `99-sessions-clock-hardening.md`.
