using FluxFlow.Components.Observability.Contracts;

namespace FluxFlow.Components.Observability.Options;

public sealed record FlowLoggerOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string Level { get; init; } = "Information";
    public string Category { get; init; } = "workflow";
    public string? MessageTemplate { get; init; }
    public string[] AttributeSelectors { get; init; } = [];
    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveCategory
        => string.IsNullOrWhiteSpace(Category) ? "workflow" : Category.Trim();

    internal string EffectiveMessageTemplate
        => string.IsNullOrWhiteSpace(MessageTemplate)
            ? "Observed {inputType} item #{sequence}."
            : MessageTemplate;

    /// <summary>
    /// Resolves <see cref="Level"/> into a <see cref="FlowLogLevel"/>. Throws
    /// <see cref="InvalidOperationException"/> for an unsupported value so a
    /// configuration mistake fails fast at construction.
    /// </summary>
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
