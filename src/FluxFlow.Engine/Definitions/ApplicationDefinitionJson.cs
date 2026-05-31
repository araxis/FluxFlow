using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Engine.Definitions;

public static class ApplicationDefinitionJson
{
    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        options.Converters.Add(new FlowNodeTypeJsonConverter());
        options.Converters.Add(new FlowPortNameJsonConverter());
        options.Converters.Add(new FlowNodeNameJsonConverter());
        options.Converters.Add(new FlowPortAddressJsonConverter());
        options.Converters.Add(new FlowLinkDefinitionJsonConverter());
        options.Converters.Add(new FlowWorkflowDefinitionJsonConverter());
        options.Converters.Add(new DashboardGridTrackDefinitionJsonConverter());

        return options;
    }

    private sealed class FlowNodeTypeJsonConverter : JsonConverter<NodeType>
    {
        public override NodeType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString() ?? throw new JsonException("Flow node type must be a string."));

        public override void Write(Utf8JsonWriter writer, NodeType value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class FlowPortNameJsonConverter : JsonConverter<PortName>
    {
        public override PortName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString() ?? throw new JsonException("Flow port name must be a string."));

        public override void Write(Utf8JsonWriter writer, PortName value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class FlowNodeNameJsonConverter : JsonConverter<NodeName>
    {
        public override NodeName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString() ?? throw new JsonException("Flow node name must be a string."));

        public override void Write(Utf8JsonWriter writer, NodeName value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class FlowPortAddressJsonConverter : JsonConverter<PortAddress>
    {
        public override PortAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => PortAddress.Parse(reader.GetString() ?? throw new JsonException("Port address must be a string."));

        public override void Write(Utf8JsonWriter writer, PortAddress value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    private sealed class FlowWorkflowDefinitionJsonConverter : JsonConverter<WorkflowDefinition>
    {
        public override WorkflowDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var nodes = JsonSerializer.Deserialize<Dictionary<string, NodeDefinition>>(ref reader, options)
                ?? throw new JsonException("Workflow definition must be a non-null JSON object.");
            return new WorkflowDefinition { Nodes = nodes };
        }

        public override void Write(Utf8JsonWriter writer, WorkflowDefinition value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Nodes, options);
    }

    private sealed class FlowLinkDefinitionJsonConverter : JsonConverter<LinkDefinition>
    {
        public override LinkDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new LinkDefinition
                {
                    From = PortAddress.Parse(reader.GetString()!)
                };
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            var from = root.TryGetProperty("from", out var f) ? f
                : root.TryGetProperty("From", out var fUpper) ? fUpper
                : throw new JsonException("Flow link object must contain a From property.");

            var fromStr = from.GetString()
                ?? throw new JsonException("Flow link 'from' must be a string.");

            var when = root.TryGetProperty("when", out var w) ? w.GetString()
                : root.TryGetProperty("When", out var wUpper) ? wUpper.GetString()
                : null;

            return new LinkDefinition
            {
                From = PortAddress.Parse(fromStr),
                When = when
            };
        }

        public override void Write(Utf8JsonWriter writer, LinkDefinition value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("from", value.From.ToString());
            if (!string.IsNullOrWhiteSpace(value.When))
                writer.WriteString("when", value.When);
            writer.WriteEndObject();
        }
    }

    private sealed class DashboardGridTrackDefinitionJsonConverter : JsonConverter<DashboardGridTrackDefinition>
    {
        public override DashboardGridTrackDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                try
                {
                    return DashboardGridTrackDefinition.Parse(reader.GetString()!);
                }
                catch (FormatException exception)
                {
                    throw new JsonException(exception.Message, exception);
                }
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var fixedSize))
            {
                return DashboardGridTrackDefinition.Fixed(fixedSize);
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Dashboard grid track must be a string, number, or object.");
            }

            if (root.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.String)
            {
                try
                {
                    return DashboardGridTrackDefinition.Parse(sizeElement.GetString()!);
                }
                catch (FormatException exception)
                {
                    throw new JsonException(exception.Message, exception);
                }
            }

            var unit = root.TryGetProperty("unit", out var unitElement) && unitElement.ValueKind == JsonValueKind.String
                ? unitElement.GetString()
                : throw new JsonException("Dashboard grid track object must contain a string 'unit' property.");

            if (!root.TryGetProperty("value", out var valueElement) ||
                valueElement.ValueKind != JsonValueKind.Number ||
                !valueElement.TryGetDouble(out var value))
            {
                throw new JsonException("Dashboard grid track object must contain a numeric 'value' property.");
            }

            return unit?.Trim().ToLowerInvariant() switch
            {
                "fixed" => DashboardGridTrackDefinition.Fixed(value),
                "percent" => DashboardGridTrackDefinition.Percent(value),
                "star" => DashboardGridTrackDefinition.Star(value),
                _ => throw new JsonException("Dashboard grid track unit must be fixed, percent, or star.")
            };
        }

        public override void Write(Utf8JsonWriter writer, DashboardGridTrackDefinition value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToSizeString());
    }
}
