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
}
