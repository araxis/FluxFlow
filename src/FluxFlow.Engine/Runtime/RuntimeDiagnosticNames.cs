namespace FluxFlow.Engine.Runtime;

/// <summary>Diagnostic names emitted by the runtime itself rather than by nodes.</summary>
public static class RuntimeDiagnosticNames
{
    public const string LinkConditionFailed = "flow.link.condition.failed";
    public const string LinkTargetRejected = "flow.link.target.rejected";
}
