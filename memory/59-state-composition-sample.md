# State Composition Sample

Date: 2026-06-01

Added `samples/FluxFlow.StateCompositionSample` to show package composition
after the first state reducer package.

The sample graph is:

1. `timer.interval` emits a finite stream of ticks.
2. `flow.mapper` maps each tick to a neutral `StateReducerInput`.
3. `state.reducer` accumulates state for the `ticks` key.
4. `flow.counter` observes reducer outputs.
5. Host-owned sinks capture reducer results and counter snapshots.

Design notes:

- The sample keeps the expression engine and sink nodes in the host.
- Package nodes stay reusable and unaware of the sample domain.
- The reducer uses an in-memory per-key state store, matching the current
  package scope.
- The graph demonstrates reliable fanout from `state.reducer` to both the
  counter and a host sink.

Verification target:

```sh
dotnet build samples/FluxFlow.StateCompositionSample/FluxFlow.StateCompositionSample.csproj /nr:false
dotnet run --project samples/FluxFlow.StateCompositionSample/FluxFlow.StateCompositionSample.csproj --no-build
```
