using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record ApplicationRuntimeBuildResult(
    ApplicationRuntime? Runtime,
    ApplicationDefinitionValidationResult Validation,
    IReadOnlyList<ApplicationRuntimeBuildError> Errors)
{
    public bool IsSuccess => Runtime is not null && Validation.IsValid && Errors.Count == 0;

    public static ApplicationRuntimeBuildResult Succeeded(
        ApplicationRuntime runtime,
        ApplicationDefinitionValidationResult validation)
        => new(runtime, validation, []);

    public static ApplicationRuntimeBuildResult Failed(
        ApplicationDefinitionValidationResult validation,
        IReadOnlyList<ApplicationRuntimeBuildError> errors)
        => new(null, validation, errors);
}
