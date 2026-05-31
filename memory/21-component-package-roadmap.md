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

- Node type constants or descriptors.
- Component implementation classes.
- Option models and option parsing helpers.
- Validation for package-specific requirements.
- Runtime factory registration.
- Package-specific diagnostic and event names.
- Focused tests and samples.
- Public README content for that component family.

The consuming application chooses which packages to reference and register.
This keeps each application free to define its own workspace JSON, section
names, and UI metadata while projecting the executable part into
`ApplicationDefinition`.

## Candidate Packages

Start with one package as the template before splitting everything:

- `FluxFlow.Components.Mqtt`: MQTT connection, trigger, publisher, metrics,
  router, filters, and MQTT-specific mapping helpers.
- `FluxFlow.Components.Files`: file writer and file-mapping helpers.
- `FluxFlow.Components.Replay`: replay source, recorder, and recorded-message
  mapping helpers.
- `FluxFlow.Components.Validation`: JSON schema validation and assertion
  helpers that are not tied to one application.
- `FluxFlow.Components.Diagnostics`: optional diagnostic sinks and adapters.

FluxMq can keep components in its own repository until the package boundary is
stable. The first extracted package should prove the pattern before other
families follow.

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

## Boundaries

Keep out of reusable component packages unless a real second consumer needs it:

- FluxMq workspace schema.
- FluxMq dashboards.
- FluxMq scenario definitions and scenario step runners.
- FluxMq storage repositories.
- FluxMq UI models.

If FluxMq scenario support becomes reusable later, consider a separate testing
package after the core component package has settled.

## Sequence

1. Record the FluxMq migration result. Done.
2. Add a small neutral consumer sample in FluxFlow.
3. Introduce a package registration helper pattern in the engine if needed.
4. Extract one package family, preferably MQTT, only after FluxMq feature work
   settles.
5. Publish the package as a prerelease and migrate FluxMq from local components
   to that package in a small follow-up branch.
