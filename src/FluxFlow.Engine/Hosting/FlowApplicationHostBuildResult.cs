using FluxFlow.Engine.Runtime;

namespace FluxFlow.Engine;

public sealed record FlowApplicationHostBuildResult(
    ApplicationRuntimeBuildResult? RuntimeBuild,
    IReadOnlyList<FlowApplicationHostBuildError> Errors)
{
    public bool IsSuccess => Errors.Count == 0 && RuntimeBuild?.IsSuccess == true;

    public static FlowApplicationHostBuildResult FromRuntime(ApplicationRuntimeBuildResult runtimeBuild)
        => new(runtimeBuild, []);

    public static FlowApplicationHostBuildResult FromHostError(FlowApplicationHostBuildError error)
        => new(null, [error]);
}
