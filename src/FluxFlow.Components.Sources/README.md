# FluxFlow.Components.Sources

Standalone typed generated and sequence sources.

- `GeneratedSourceNode<T>` emits configured T items with optional looping and
  timing. `GeneratedSourceNode` is its `JsonElement` specialization.
- `SequenceSourceNode` emits typed `SequenceItem` values for a numeric sequence.

Both expose Output, Events, Completion, start/stop lifecycle, bounded internal
delivery, and an optional host-owned `TimeProvider`. Source startup failures are
`FlowError` messages where continuation is possible; unrecoverable lifecycle
faults remain on Completion. There is no Errors output.

## Composition

Install `FluxFlow.Components.Sources.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
