# FluxFlow.Components.Observability

Standalone generic counter, logger, and measurement nodes.

| Node | Typed contract |
|------|----------------|
| `FlowCounterNode<T>` | T -> `FlowCounterSnapshot` |
| `FlowLoggerNode<T>` | T -> `FlowLogEntry<T>` |
| `FlowMetricsNode<T>` | T -> `FlowMetricSnapshot` |

The non-generic names are explicit `JsonElement` specializations. Predicates,
expressions, and `IObservabilityValueSelector<T>` receive the declared input
without universal normalization. Logger entries preserve typed input and
selected attributes.

Rejected counter input and normal snapshots are typed outcomes. Evaluation,
selector, or operational failure becomes `FlowError` on Output. Events remains
the diagnostic stream; no Engine or Composition package is required.

## Composition

Install `FluxFlow.Components.Observability.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
