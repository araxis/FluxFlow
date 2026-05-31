# Removal Map

Date: 2026-05-31

## Keep in `FluxFlow.Engine`

- Definition model for resources, workflows, nodes, ports, and links.
- Runtime graph builder.
- Typed `InputPort<T>` and `OutputPort<T>` wrappers.
- `IFlowNode`, `IFlowEventSource`, `FlowEvent`, and `FlowError`.
- Runtime host lifecycle.
- Generic expression mapping contracts and engines, unless the package is later split into `FluxFlow.Mapping`.
- Generic scenario runner with `expect.event`, if tests remain part of the base package.

## Remove from `FluxFlow.Engine`

- Transport-specific scenario step types.
- Transport-specific scenario configuration keys.
- Component event type constants.
- Source-application configuration names.
- Component validation such as connection, QoS, retain, subscriptions, and payload encoding.
- Any examples that require a particular broker, web call, file sink, database, or desktop shell.

## Move to companion packages later

- Dashboard and designer metadata: `FluxFlow.Designer` or `FluxFlow.UI`.
- Scenario steps for transports or protocols: `FluxFlow.Scenarios.*`.
- Transport components: separate packages per connector family.
- Storage helpers: separate storage packages.
- Mapping engines, if package size or dependency control becomes a concern.
