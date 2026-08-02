using System.Text.Json.Serialization;

namespace FluxFlow.Composition;

public sealed record CompositionProcessingProfile
{
    public CompositionProcessingMode Mode { get; init; } = CompositionProcessingMode.Sequential;

    public CompositionProcessingOrder Order { get; init; } = CompositionProcessingOrder.Preserve;

    public CompositionProcessingBuffer Buffer { get; init; } = CompositionProcessingBuffer.Standard;
}

[JsonConverter(typeof(JsonStringEnumConverter<CompositionProcessingMode>))]
public enum CompositionProcessingMode
{
    Sequential,
    Parallel
}

[JsonConverter(typeof(JsonStringEnumConverter<CompositionProcessingOrder>))]
public enum CompositionProcessingOrder
{
    Preserve,
    Relaxed
}

[JsonConverter(typeof(JsonStringEnumConverter<CompositionProcessingBuffer>))]
public enum CompositionProcessingBuffer
{
    Small,
    Standard,
    Large
}
