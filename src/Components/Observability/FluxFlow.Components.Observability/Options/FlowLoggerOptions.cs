using FluxFlow.Components.Observability.Contracts;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Observability.Options;

public sealed record FlowLoggerOptions
{
    public string Level { get; init; } = "Information";

    public string Category { get; init; } = "workflow";

    public string? MessageTemplate { get; init; }

    [JsonConverter(typeof(OneOrManyStringJsonConverter))]
    public string[] AttributeSelectors { get; init; } = [];

    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveCategory
        => string.IsNullOrWhiteSpace(Category) ? "workflow" : Category.Trim();

    internal string EffectiveMessageTemplate
        => string.IsNullOrWhiteSpace(MessageTemplate)
            ? "Observed item #{sequence}."
            : MessageTemplate;

    internal FlowLogLevel ResolveLevel()
    {
        if (string.IsNullOrWhiteSpace(Level) ||
            !Enum.TryParse<FlowLogLevel>(Level, ignoreCase: true, out var level))
        {
            throw new InvalidOperationException(
                $"flow.logger option 'level' contains unsupported value '{Level}'.");
        }

        return level;
    }
}
