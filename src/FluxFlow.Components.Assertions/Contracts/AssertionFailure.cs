namespace FluxFlow.Components.Assertions.Contracts;

public sealed record AssertionFailure
{
    public required string Description { get; init; }
    public required string Message { get; init; }
    public required string Expression { get; init; }
    public required string InputType { get; init; }
    public object? Value { get; init; }
}
