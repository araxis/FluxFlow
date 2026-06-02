namespace FluxFlow.Components.Routing.Diagnostics;

public static class RoutingDiagnosticNames
{
    public const string SwitchRouted = "flow.switch.routed";
    public const string SwitchFailed = "flow.switch.failed";

    public const string CorrelationMatched = "flow.correlation.matched";
    public const string CorrelationTimedOut = "flow.correlation.timedOut";
    public const string CorrelationFailed = "flow.correlation.failed";

    public const string WindowEmitted = "flow.window.emitted";
    public const string WindowFailed = "flow.window.failed";
}
