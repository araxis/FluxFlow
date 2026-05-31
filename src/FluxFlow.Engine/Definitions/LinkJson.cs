using System.Text.Json;

namespace FluxFlow.Engine.Definitions;

public static class LinkJson
{
    public static IReadOnlyList<LinkDefinition> ParseMany(
        JsonElement value,
        string workflowName,
        string? defaultWhen = null)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => ParseOne(item, workflowName, defaultWhen))
                .ToArray();
        }

        return [ParseOne(value, workflowName, defaultWhen)];
    }

    public static LinkDefinition ParseOne(
        JsonElement value,
        string workflowName,
        string? defaultWhen = null)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => new LinkDefinition
            {
                From = PortAddress.ExpandAndParse(value.GetString()!, workflowName),
                When = defaultWhen
            },
            JsonValueKind.Object => ParseObject(value, workflowName, defaultWhen),
            _ => throw new JsonException("Flow link must be a string or object.")
        };
    }

    private static LinkDefinition ParseObject(JsonElement value, string workflowName, string? defaultWhen)
    {
        var from = ReadProperty(value, "from") ?? ReadProperty(value, "From");
        if (from is null)
            throw new JsonException("Flow link object must contain a From property.");

        var fromStr = from.Value.GetString()
            ?? throw new JsonException("Flow link 'from' must be a string.");

        var when = ReadProperty(value, "when") ?? ReadProperty(value, "When");

        return new LinkDefinition
        {
            From = PortAddress.ExpandAndParse(fromStr, workflowName),
            When = when?.GetString() ?? defaultWhen
        };
    }

    private static JsonElement? ReadProperty(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) ? property : null;
}
