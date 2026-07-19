namespace FluxFlow.Components.Assertions.Options;

/// <summary>
/// Configuration for the canonical <c>FlowValue</c> assertion node.
/// </summary>
public sealed record FlowValueAssertionOptions
{
    public string? Engine { get; init; }

    public string? Expression { get; init; }

    public string? ExpressionId { get; init; }

    public string? ExpressionName { get; init; }

    public string InputType { get; init; } = AssertionOptions.ObjectTypeName;

    public int BoundedCapacity { get; init; } = 128;

    public string? Description { get; init; }

    public string? FailureMessage { get; init; }

    internal string EffectiveDescription
        => string.IsNullOrWhiteSpace(Description)
            ? AssertionOptions.DefaultDescription
            : Description.Trim();

    internal string EffectiveFailureMessage
        => string.IsNullOrWhiteSpace(FailureMessage)
            ? AssertionOptions.DefaultFailureMessage
            : FailureMessage.Trim();
}
