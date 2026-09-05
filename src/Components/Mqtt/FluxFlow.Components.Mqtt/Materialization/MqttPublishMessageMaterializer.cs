using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Materialization;

public sealed class MqttClientRequestMaterializer()
    : JsonFlowValueMaterializer<FluxFlow.Components.Mqtt.Client.MqttClientRequest>(
        "mqtt.command.invalid_shape",
        "MQTT",
        FlowValueShape.Object(
            "An MQTT client command object whose operation selects connect, disconnect, status, publish, subscribe, or unsubscribe.",
            "operation"));

/// <summary>Owns the structural contract accepted by the MQTT publish component.</summary>
public sealed class MqttPublishMessageMaterializer : IFlowValueMaterializer<MqttPublishMessage>
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public FlowValueShape InputShape { get; } = FlowValueShape.Object(
        "An MQTT publish object with topic, content, and optional QoS, retain, response-topic, correlation, and user-property fields.",
        "topic",
        "content");

    public FlowValueMaterializationResult<MqttPublishMessage> Materialize(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            var request = JsonNode.Parse(value.ToString()) as JsonObject
                ?? throw new JsonException("MQTT publish input must be an object.");
            NormalizeContent(request);
            var materialized = request.Deserialize<MqttPublishMessage>(SerializerOptions)
                ?? throw new JsonException("MQTT publish input cannot be null.");
            return FlowValueMaterializationResult<MqttPublishMessage>.Success(materialized);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return FlowValueMaterializationResult<MqttPublishMessage>.Failure(
                new FlowError(
                    "mqtt.publish.invalid_shape",
                    "The workflow value cannot be materialized as an MQTT publish request.",
                    "MQTT",
                    details: JsonSerializer.SerializeToElement(new
                    {
                        target = nameof(MqttPublishMessage),
                        reason = exception.Message
                    })));
        }
    }

    private static void NormalizeContent(JsonObject request)
    {
        var contentProperty = FindProperty(request, "content");
        if (contentProperty is null || request[contentProperty] is not { } content)
            return;

        request[contentProperty] = JsonSerializer.SerializeToNode(
            ToContent(content),
            SerializerOptions);
    }

    private static FlowContent ToContent(JsonNode content)
    {
        if (content is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            return FlowContent.FromBytes(
                Encoding.UTF8.GetBytes(text),
                "text/plain",
                "utf-8");
        }

        if (content is JsonObject objectContent && FindProperty(objectContent, "bytes") is not null)
        {
            return objectContent.Deserialize<FlowContent>(SerializerOptions)
                ?? throw new JsonException("MQTT publish content cannot be null.");
        }

        return FlowContent.FromBytes(
            Encoding.UTF8.GetBytes(content.ToJsonString()),
            "application/json",
            "utf-8");
    }

    private static string? FindProperty(JsonObject value, string name)
    {
        foreach (var property in value)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
                return property.Key;
        }

        return null;
    }
}
