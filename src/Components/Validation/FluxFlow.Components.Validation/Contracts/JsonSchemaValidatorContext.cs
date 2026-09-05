namespace FluxFlow.Components.Validation.Contracts;

/// <summary>
/// Context handed to a value selector for each validation. Carries the configured
/// input type and the selector name so a selector can branch on them. Engine-free:
/// no node address or runtime coupling.
/// </summary>
public sealed record JsonSchemaValidatorContext
{
    public required Type InputType { get; init; }
    public string? ValueSelector { get; init; }
}
