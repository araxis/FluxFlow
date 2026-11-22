# FluxFlow.Core.Mapping

The shared expression boundary consists of two types:

- `IFlowExpressionEngine` compiles an expression into `Func<FlowMapContext, T>`.
- `FlowMapContext` carries per-evaluation variables. `ForMessage` supplies the original
  message, payload aliases, value, headers, string IDs and timestamp.

Setup resolves a keyed engine, compiles once during Build, and supplies ordinary
delegates to components. Mapper returns FlowValue; Router and conditional links
evaluate booleans. Components do not depend on an engine or context factory.
Plain C# component callbacks need no expression interface or adapter.

Engine packages depend on Core. File loading and configuration registration belong
in Setup. FlowMapContext snapshots caller-supplied dictionaries with ordinal keys;
it does not deep-copy their values. ForMessage owns its fresh dictionary directly,
without an extra copy. Each evaluation still gets independent variables.

The default Compile implementation delegates to Evaluate on each invocation.
Engines that parse/compile expressions override it for reuse; bundled engines do so.

The former mapper/predicate adapters and context-factory interfaces are removed.
Consumers use delegates and the shared engine directly; there is no replacement layer.
