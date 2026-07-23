namespace FluxFlow.Components.Assertions.Options;

/// <summary>
/// Configuration for the canonical <c>FlowValue</c> assertion node.
/// </summary>
public sealed record FlowValueAssertionOptions
{
    public const string ObjectTypeName = "object";

    public const string DefaultDescription = "Flow assertion";

    public const string DefaultFailureMessage = "Assertion failed.";

    public string? Expression { get; init; }

    public string? ExpressionId { get; init; }

    public string? ExpressionName { get; init; }

    public string InputType { get; init; } = ObjectTypeName;

    public int BoundedCapacity { get; init; } = 128;

    public string? Description { get; init; }

    public string? FailureMessage { get; init; }

    internal string EffectiveDescription
        => string.IsNullOrWhiteSpace(Description)
            ? DefaultDescription
            : Description.Trim();

    internal string EffectiveFailureMessage
        => string.IsNullOrWhiteSpace(FailureMessage)
            ? DefaultFailureMessage
            : FailureMessage.Trim();
}
