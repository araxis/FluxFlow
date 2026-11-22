using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Core.Messaging;
using NATS.Client.JetStream;

namespace FluxFlow.Components.Nats.JetStream;

public sealed record JetStreamDelivery(string SettlementToken, NatsMessage Message, NatsJSMsgMetadata Metadata);
public sealed record JetStreamPublished(NatsMessage Message, string Stream, ulong Sequence, bool Duplicate);
public sealed record JetStreamSettlement(string SettlementToken, string Action = "Ack", int DelaySeconds = 0);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JetStreamDelivery))]
[JsonSerializable(typeof(JetStreamPublished))]
[JsonSerializable(typeof(JetStreamSettlement))]
internal partial class JetStreamJson : JsonSerializerContext
{
    internal static JsonFlowValue Value<T>(T value, JsonTypeInfo<T> type) => new(JsonSerializer.SerializeToElement(value, type));
    internal static JetStreamSettlement Read(FlowMessage message) => message.Value.Json.Deserialize(Default.JetStreamSettlement)
        ?? throw new ArgumentException("Expected a JetStream settlement command.");
}
