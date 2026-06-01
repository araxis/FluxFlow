namespace FluxFlow.Components.Assertions.Options;

public sealed record AssertionOptions
{
    public const string ObjectTypeName = "object";
    public const string DefaultDescription = "Flow assertion";
    public const string DefaultFailureMessage = "Assertion failed.";

    public string? Engine { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;
    public string? Description { get; init; }
    public string? FailureMessage { get; init; }
    public bool EmitPassedInput { get; init; } = true;
    public bool EmitFailedInput { get; init; } = true;

    internal string EffectiveDescription
        => string.IsNullOrWhiteSpace(Description)
            ? DefaultDescription
            : Description.Trim();

    internal string EffectiveFailureMessage
        => string.IsNullOrWhiteSpace(FailureMessage)
            ? DefaultFailureMessage
            : FailureMessage.Trim();
}
