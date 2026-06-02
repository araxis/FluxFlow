namespace FluxFlow.Components.Routing.Options;

public sealed record JoinRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string? Engine { get; init; }
    public string? LeftKeyExpression { get; init; }
    public string? RightKeyExpression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string LeftInputType { get; init; } = ObjectTypeName;
    public string RightInputType { get; init; } = ObjectTypeName;
    public bool CaseSensitive { get; init; } = true;
    public int TimeoutMilliseconds { get; init; } = 30_000;
    public int MaxPending { get; init; } = 1_024;
    public int BoundedCapacity { get; init; } = 128;
}
