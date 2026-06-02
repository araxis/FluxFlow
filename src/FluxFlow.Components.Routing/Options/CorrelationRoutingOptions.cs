namespace FluxFlow.Components.Routing.Options;

public sealed record CorrelationRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string? Engine { get; init; }
    public string? KeyExpression { get; init; }
    public string? SideExpression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string RequestSide { get; init; } = "request";
    public string ResponseSide { get; init; } = "response";
    public bool CaseSensitive { get; init; } = true;
    public int TimeoutMilliseconds { get; init; } = 30_000;
    public int MaxPending { get; init; } = 1_024;
    public int BoundedCapacity { get; init; } = 128;
}
