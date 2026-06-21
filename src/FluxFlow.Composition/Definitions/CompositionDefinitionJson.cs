using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Composition;

public static class CompositionDefinitionJson
{
    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        options.Converters.Add(new NodeReferenceJsonConverter());
        options.Converters.Add(new PortReferenceJsonConverter());
        return options;
    }

    private sealed class NodeReferenceJsonConverter : JsonConverter<NodeReference>
    {
        public override NodeReference Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return NodeReference.Parse(reader.GetString()!);

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Node references must be strings or objects.");

            string? workflow = null;
            string? node = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a node reference property name.");

                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "workflow":
                        workflow = reader.GetString();
                        break;
                    case "node":
                        node = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(node))
                throw new JsonException("Node references require a node value.");

            return new NodeReference { Workflow = workflow, Node = node };
        }

        public override void Write(Utf8JsonWriter writer, NodeReference value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    private sealed class PortReferenceJsonConverter : JsonConverter<PortReference>
    {
        public override PortReference Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return PortReference.Parse(reader.GetString()!);

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Port references must be strings or objects.");

            string? workflow = null;
            string? node = null;
            string? port = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a port reference property name.");

                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "workflow":
                        workflow = reader.GetString();
                        break;
                    case "node":
                        node = reader.GetString();
                        break;
                    case "port":
                        port = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(node) || string.IsNullOrWhiteSpace(port))
                throw new JsonException("Port references require node and port values.");

            return new PortReference { Workflow = workflow, Node = node, Port = port };
        }

        public override void Write(Utf8JsonWriter writer, PortReference value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
