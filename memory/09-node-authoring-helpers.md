# Node Authoring Helpers

Date: 2026-05-31

## Decision

Add helper types without changing the existing runtime contracts. Direct
`IFlowNode`, `InputPort<T>`, `OutputPort<T>`, and `RuntimeNode.Create` usage remain
valid.

## Added Helpers

- `FlowNodeBase`: shared `Id`, `Completion`, and error stream ownership.
- `EventFlowNodeBase`: event stream ownership for nodes that emit observations.
- `SourceFlowNode<TOutput>`: output-buffer source base.
- `SinkFlowNode<TInput>`: input handling base with recoverable error reporting.
- `TransformFlowNode<TInput,TOutput>`: multi-output transform base.
- `MapFlowNode<TInput,TOutput>`: one-output transform base.
- `RuntimeNodeBuilder`: fluent factory helper for registering ports.
- `RuntimeNodeFactoryContextExtensions`: small helpers for port addresses and builder creation.
- `IFlowNodeRegistration`: component-package registration contract.
- `RuntimeNodeFactoryRegistryExtensions`: registration helpers for one or more registration objects.

## Design Rules

- Helpers are optional.
- Helpers should remove repeated code, not hide the graph model.
- Exceptions during sink/transform message handling are reported to `Errors` and the node keeps running.
- Component packages still own their own configuration, validation, and event names.
