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

            var fromStr = ReadString(from, "Flow link 'from' must be a string.");

            var when = root.TryGetProperty("when", out var w) ? ReadOptionalString(w, "Flow link 'when' must be a string.")
                : root.TryGetProperty("When", out var wUpper) ? ReadOptionalString(wUpper, "Flow link 'when' must be a string.")
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

        private static string ReadString(JsonElement value, string errorMessage)
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new JsonException(errorMessage);

            return value.GetString()
                ?? throw new JsonException(errorMessage);
        }

        private static string? ReadOptionalString(JsonElement value, string errorMessage)
        {
            if (value.ValueKind == JsonValueKind.Null)
                return null;

            return ReadString(value, errorMessage);
        }
    }

}
