# FluxFlow.Engine Docs

The package README is the current public documentation for the standalone engine.
The neutral consumer sample lives at `samples/FluxFlow.SampleApp`.

The previous detailed docs were moved to `memory/legacy-docs` because they still
describe source-app concerns and older public APIs. Keep them as reference while
rewriting the package docs around the current boundary.

## Rewrite Order

1. Getting started with neutral sample nodes.
2. Definitions and runtime model.
3. Node authoring and package extension model.
4. Host lifecycle and diagnostics.
5. Workspace projection patterns for consuming applications.
6. Generated API reference from the current source.

## Current Rules

- Use only protocol-neutral examples in public docs.
- Put planning notes and extraction history under `memory`.
- Keep component package ideas out of the base package docs until those packages exist.
- Regenerate API reference after every public API change.

## Current Node Authoring Helpers

- `FlowNodeBase`: owns `Id`, `Completion`, and `Errors`.
- `EventFlowNodeBase`: adds an event stream and `EmitEvent` helpers.
- `SourceFlowNode<TOutput>`: source node with an output buffer.
- `SinkFlowNode<TInput>`: sink node with input handling and error reporting.
- `TransformFlowNode<TInput,TOutput>`: multi-output transform node.
- `MapFlowNode<TInput,TOutput>`: one-output transform node.
- `RuntimeNodeBuilder`: fluent input/output registration for node factories.
- `IFlowNodeRegistration`: package-friendly factory registration contract.
- `ExpressionFlowPredicate<TInput>`: expression-backed predicate for link
  conditions and custom routing helpers. It can use the default `input` and
  `value` variables or a custom `IFlowMapContextFactory<TInput>`.
