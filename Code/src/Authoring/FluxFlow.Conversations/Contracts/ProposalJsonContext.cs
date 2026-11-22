using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Application.Addressing;

namespace FluxFlow.Conversations;

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowOutOfOrderMetadataProperties = true,
    Converters = [typeof(ComponentAddressConverter), typeof(PortAddressConverter)])]
[JsonSerializable(typeof(ProposalRequest))]
[JsonSerializable(typeof(ProposalResult))]
[JsonSerializable(typeof(ProposalUpdate))]
[JsonSerializable(typeof(ProposalStep[]))]
[JsonSerializable(typeof(ProposalConnectionRequest))]
[JsonSerializable(typeof(ProposalConnectionStatus))]
public sealed partial class ProposalJsonContext : JsonSerializerContext
{
    // Request bodies are application/json, never HTML. Keep base64 '+' characters compact.
    public static ProposalJsonContext Transport => TransportContext.Value;
    private static readonly Lazy<ProposalJsonContext> TransportContext = new(() => new(new JsonSerializerOptions(Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));
}

public sealed class ComponentAddressConverter : JsonConverter<ComponentAddress>
{
    public override bool HandleNull => true;
    public override ComponentAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        ComponentAddress.Parse(reader.GetString() ?? throw new JsonException("A component address is required."));
    public override void Write(Utf8JsonWriter writer, ComponentAddress value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class PortAddressConverter : JsonConverter<PortAddress>
{
    public override bool HandleNull => true;
    public override PortAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        PortAddress.Parse(reader.GetString() ?? throw new JsonException("A port address is required."));
    public override void Write(Utf8JsonWriter writer, PortAddress value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}
