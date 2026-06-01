namespace FluxFlow.Components.Control.Options;

public sealed record ControlExpressionOptions
{
    public const string ObjectTypeName = "object";
    public const string DefaultAssertName = "Flow assertion";
    public const string DefaultFailureMessage = "Assertion failed.";

    public string? Engine { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;
    public string? Name { get; init; }
    public string? FailureMessage { get; init; }

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? DefaultAssertName : Name.Trim();

    internal string EffectiveFailureMessage
        => string.IsNullOrWhiteSpace(FailureMessage)
            ? DefaultFailureMessage
            : FailureMessage.Trim();
}
