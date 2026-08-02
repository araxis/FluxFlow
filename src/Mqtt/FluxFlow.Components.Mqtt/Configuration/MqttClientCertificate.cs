using System.Collections.Immutable;

namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttClientCertificate
{
    private ImmutableArray<byte> _content = ImmutableArray<byte>.Empty;

    public required string Name { get; init; }

    public ReadOnlyMemory<byte> Content
    {
        get => _content.AsMemory();
        init => _content = ImmutableArray.CreateRange(value.ToArray());
    }

    public string? Password { get; init; }
}
