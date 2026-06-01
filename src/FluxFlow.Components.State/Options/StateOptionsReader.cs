using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.State.Options;

internal static class StateOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static StateReducerOptions ReadReducerOptions(NodeDefinition definition)
    {
        var options = Read<StateReducerOptions>(definition);

        if (string.IsNullOrWhiteSpace(options.Reducer))
        {
            throw new InvalidOperationException(
                "state.reducer option 'reducer' is required.");
        }

        if (options.KeyExpression is not null &&
            string.IsNullOrWhiteSpace(options.KeyExpression))
        {
            throw new InvalidOperationException(
                "state.reducer option 'keyExpression' cannot be empty when set.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "state.reducer option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxKeys < 0)
        {
            throw new InvalidOperationException(
                "state.reducer option 'maxKeys' must be zero or greater.");
        }

        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }
}
