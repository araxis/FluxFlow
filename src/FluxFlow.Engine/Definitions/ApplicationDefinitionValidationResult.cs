namespace FluxFlow.Engine.Definitions;

public sealed record ApplicationDefinitionValidationResult(IReadOnlyList<ApplicationDefinitionValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
