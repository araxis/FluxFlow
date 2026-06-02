namespace FluxFlow.Components.Routing;

public static class RoutingErrorCodes
{
    public const int SwitchExpressionFailed = 18000;

    public const int CorrelationKeyFailed = 18100;
    public const int CorrelationInvalidKey = 18105;
    public const int CorrelationSideFailed = 18110;
    public const int CorrelationInvalidSide = 18120;
    public const int CorrelationDuplicateSide = 18130;
    public const int CorrelationCapacityExceeded = 18140;

    public const int WindowFailed = 18200;

    public const int JoinLeftKeyFailed = 18300;
    public const int JoinRightKeyFailed = 18310;
    public const int JoinInvalidKey = 18320;
    public const int JoinCapacityExceeded = 18330;
    public const int JoinFailed = 18340;
}
