using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mapping.Options;

public sealed record MapperOptions
{
    public const string ObjectTypeName = "object";

    public string? Engine { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string OutputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;

    [JsonPropertyName("targetType")]
    public string? TargetType { get; init; }

    internal string EffectiveOutputType
        => string.IsNullOrWhiteSpace(OutputType) || OutputType == ObjectTypeName
            ? TargetType ?? OutputType
            : OutputType;
}
