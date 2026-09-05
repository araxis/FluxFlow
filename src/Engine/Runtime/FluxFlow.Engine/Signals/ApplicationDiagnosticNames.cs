namespace FluxFlow.Engine.Signals;

public static class ApplicationDiagnosticNames
{
    public const string InputAccepted = "flow.port.input.accepted";
    public const string OutputEmitted = "flow.port.output.emitted";
    public const string PortRejected = "flow.port.rejected";
    public const string RequestCompleted = "flow.port.request.completed";
    public const string SystemEventDeliveryFailed = "flow.system.event.delivery.failed";
}
