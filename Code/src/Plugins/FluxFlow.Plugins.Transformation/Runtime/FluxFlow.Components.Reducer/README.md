# FluxFlow.Components.Reducer

One component, one private immutable state. Reduce(currentState, input) produces
a candidate. AreEqual(currentState, candidate) decides whether it changed. An equal
candidate is neither stored nor emitted. A changed candidate becomes the state and
is emitted with the triggering input's trace/correlation/headers and causation.
Initial state is not emitted automatically. No grouping, windows or persistence.

```csharp
await using var reducer = new Reducer(new ReducerOptions
{
    InitialState = initialState,
    Reduce = (state, input) => ComputeNextState(state, input),
    AreEqual = (previous, candidate) => CompareStates(previous, candidate)
});
upstream.Output.LinkTo(reducer.Input);
reducer.Output.LinkTo(downstream.Input);
```

Equality is deliberately explicit; neither CLR reference equality nor JSON text
equality is assumed. Delegates can use concrete FlowValue subclasses directly,
without JSON conversions. Values/arguments must be immutable; the component cannot
roll back mutation performed by user code. Reduce or comparison exceptions leave
the previous state intact and become normal processing errors. Incoming FlowError
messages bypass reduction without touching state. CLR null results are errors;
JsonFlowValue.Null is a valid state. A returned FlowError is itself a candidate
state, so throw an exception for a failed reduction that must not update state.

Processing is serial and ordered. Native Execution options remain available for
capacity, scheduler and cancellation, but parallelism other than one or unordered
processing is rejected. OutputCapacity defaults to 128. Complete drains accepted
work; cancellation/fault uses the existing Core lifecycle. Separate instances
keep separate state references, even when they share immutable initial values.

This is change-detecting reduction, not Distinct: a state may legitimately return
to an older value and be emitted again. State is in-memory only; no reset or external
state access API is added without a requirement. No application registration here.
