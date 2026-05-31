# Component Package Template Plan

Date: 2026-05-31

## Goal

Create the first separate component package in a way that becomes the template
for future component families.

The broader component catalog and reusable per-component definition template are
recorded in `31-component-catalog-and-template.md`.

The package should prove:

- package-owned node type names
- package-owned options and validation
- explicit registration through `IFlowNodeModule`
- no assembly scanning
- no hidden global state
- diagnostics and events emitted through engine contracts
- focused tests without requiring live external services
- a small sample or fixture that documents consumer usage

## First Candidate

Start with `FluxFlow.Components.Mqtt`.

Reason:

- FluxMq already has real MQTT components and real integration pressure.
- The engine boundary was already validated by the FluxMq migration.
- A messaging component package will exercise sources, sinks, options,
  diagnostics, and event patterns.
- Other component families can copy the same module/registration shape.

## Proposed Project Shape

```text
src/
  FluxFlow.Components.Mqtt/
    FluxFlow.Components.Mqtt.csproj
    MqttComponentTypes.cs
    MqttComponentModule.cs
    MqttComponentRegistrationExtensions.cs
    Options/
      MqttComponentOptions.cs
      MqttConnectionProfile.cs
      MqttSubscriptionOptions.cs
      MqttPublishOptions.cs
    Contracts/
      IMqttClientAdapter.cs
      IMqttClientFactory.cs
      MqttMessage.cs
    Nodes/
      MqttSubscribeNode.cs
      MqttPublishNode.cs
    Diagnostics/
      MqttDiagnosticNames.cs
      MqttEventNames.cs

tests/
  FluxFlow.Components.Mqtt.Tests/
    MqttComponentModuleTests.cs
    MqttSubscribeNodeTests.cs
    MqttPublishNodeTests.cs
    MqttOptionsTests.cs
```

Keep any real network client behind `IMqttClientAdapter`. Tests should use an
in-memory adapter so the package template stays deterministic.

## Registration Shape

Desired consumer code:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMqttComponents(options => options
        .UseClientFactory(profile => clientFactory.Create(profile)));
```

Package registration should:

- validate required delegates before registering nodes
- register every node type through `IFlowNodeModule`
- return the same `RuntimeNodeFactoryRegistry`
- avoid service locators and static mutable configuration

## Node Type Naming

Use stable package-owned node type names:

```text
mqtt.subscribe
mqtt.publish
```

Keep persisted type names lowercase and dotted. Do not rename them after a
package is published unless a migration path exists.

## Options Boundary

Each node should read only its own configuration from `NodeDefinition`:

- subscribe node: connection profile, topic filter, payload format, optional
  event channel
- publish node: connection profile, target topic, payload mapping mode, retain
  flag, quality setting

Validation should produce clear package-level errors before runtime startup
where possible.

## Runtime Behavior

Subscribe node:

- starts a client subscription during `StartAsync`
- emits messages through an output port
- emits diagnostics for connect, subscribe, receive, reconnect, and stop
- faults or completes through normal engine lifecycle methods

Publish node:

- receives messages through an input port
- publishes through the adapter
- emits diagnostics for send success/failure
- emits events only for workflow-relevant publish activity

## Release Automation Impact

The first component package requires release workflow changes:

- pack more than one project
- upload all package artifacts
- publish all package artifacts
- keep changelog sections clear for multi-package releases

Initial component release can use the same prerelease version as the engine.

## Implementation Steps

1. Scaffold the component package and test project.
2. Add adapter contracts and in-memory test adapter.
3. Add options models and parsing helpers.
4. Add module and registry extension.
5. Implement publish node first because it is easier to test deterministically.
6. Implement subscribe node with in-memory adapter tests.
7. Add package README content and docs link.
8. Update release workflow to pack all selected packages.
9. Publish a prerelease.
10. Migrate FluxMq components to the package in a separate FluxMq branch.

## Open Decisions

- Whether the first package ships only contracts plus nodes, or also includes a
  default client adapter.
- Whether payload mapping belongs in this package or a later data/mapping
  package.
- Whether multi-package releases share one changelog section or separate
  package subsections.

## Recommendation

Start with contracts, options, module registration, publish node, subscribe
node, and deterministic tests. Defer a default live client adapter until the
package shape is proven by FluxMq.
