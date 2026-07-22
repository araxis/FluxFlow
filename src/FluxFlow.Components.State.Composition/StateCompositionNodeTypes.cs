using FluxFlow.Composition;

namespace FluxFlow.Components.State.Composition;

public static class StateCompositionNodeTypes
{
    public const string Reducer = "state.reduce";
    public const string LegacyReducer = "state.reducer";

    internal static CompositionComponentTypeDescriptor ReducerDescriptor { get; } =
        new(Reducer, [LegacyReducer]);
}
