# FluxFlow State Composition Sample

This sample composes reusable packages without app-specific component logic:

1. `timer.interval` emits three ticks.
2. `flow.mapper` maps each tick into a `StateReducerInput`.
3. `state.reducer` keeps a per-key count.
4. `flow.counter` observes reducer outputs.
5. Host-owned sink nodes capture the final state and counter snapshots.

Run it from the repository root:

```sh
dotnet build samples/FluxFlow.StateCompositionSample/FluxFlow.StateCompositionSample.csproj /nr:false
dotnet run --project samples/FluxFlow.StateCompositionSample/FluxFlow.StateCompositionSample.csproj --no-build
```

Expected shape:

```text
Sample: state-composition
State updates: 3
Counter snapshots: 3
Final state: key=ticks value=3 version=3
Final counter: 3
```
