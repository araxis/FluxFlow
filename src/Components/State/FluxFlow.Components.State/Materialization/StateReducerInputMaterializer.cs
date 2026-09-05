using System.Text.Json;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.State.Materialization;

public sealed class StateReducerInputMaterializer()
    : JsonFlowValueMaterializer<StateReducerInput<JsonElement>>(
        "state.reducer.invalid_shape",
        "State",
        FlowValueShape.Object(
            "A state reducer request object with key, input, initial state, variables, and operation.",
            "key"));
