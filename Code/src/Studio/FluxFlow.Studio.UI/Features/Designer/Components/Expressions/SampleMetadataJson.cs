using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Studio.UI.Features.Designer.Components.Expressions;

// The editor shows expression variables; the host receives native message properties.
public static class SampleMetadataJson
{
    private static readonly (string Property, string Variable)[] Fields =
    [
        ("TraceId", "$traceId"), ("MessageId", "$messageId"),
        ("CausationId", "$causationId"), ("CorrelationId", "$correlationId"),
        ("Timestamp", "$timestamp")
    ];

    public static IReadOnlyList<string> Properties { get; } =
        Array.AsReadOnly(Fields.Select(field => field.Variable).Append("$headers").ToArray());

    public static JsonObject Create(string context, string headers)
    {
        var message = JsonNode.Parse(context)!.AsObject();
        var result = new JsonObject();
        foreach (var (property, variable) in Fields) result[variable] = message[property]?.DeepClone();
        result["$headers"] = JsonNode.Parse(headers);
        return result;
    }

    public static (string Context, string Headers) Read(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var context = new JsonObject();
        foreach (var (property, variable) in Fields)
            context[property] = JsonNode.Parse(root.GetProperty(variable).GetRawText());
        return (context.ToJsonString(), root.GetProperty("$headers").GetRawText());
    }
}
