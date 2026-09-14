# FluxFlow.Components.Observability

Optional counter, logger, and measurement components. All inputs and outputs use
`FlowMessage` with immutable `FlowValue`; type parameters describe local processing.

| Node | Local processing |
|------|----------------|
| `FlowCounterNode<T>` | T -> `FlowCounterSnapshot` |
| `FlowLoggerNode<T>` | T -> `FlowLogEntry<T>` |
| `FlowMetricsNode<T>` | T -> `FlowMetricSnapshot` |

The non-generic names are `JsonElement` specializations of the same implementations,
not conversion wrappers. Predicates and `IObservabilityValueSelector<T>` receive
the value materialized as T inside the component. Outputs snapshot results into
owned structured values, including logger input and selected attributes.

Construct each component directly and add its instance to a workflow. Core owns
the shared bounded input, reliable output, events and lifecycle. There are no
additional response queues or event relays in this family.

Rejected counter input and normal snapshots are valid outcomes. Materialization, evaluation,
selector, or operational failure becomes `FlowError` on Output. Events remains
the diagnostic stream; no Engine or Composition package is required. Incoming
errors retain their exact message identity and do not invoke selectors.

These optional workflow calculations do not add a reporting policy or telemetry
SDK to Core. Version 9.0.0-alpha.1 is an unpublished development preview, not a
release candidate.

## Composition

Install `FluxFlow.Components.Observability.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
