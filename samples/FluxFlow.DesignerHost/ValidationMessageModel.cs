namespace FluxFlow.DesignerHost;

/// <summary>
/// Host-local view of one validation result for a status list. Metadata problems
/// and application-link diagnostics land in the same shape so a status view renders one
/// list regardless of the source.
/// </summary>
public sealed record ValidationMessageModel
{
    public required ValidationSeverity Severity { get; init; }
    public required ValidationSource Source { get; init; }
    public required string Message { get; init; }
    public string? ComponentType { get; init; }
    public string? ComponentName { get; init; }
}

public enum ValidationSeverity
{
    Information,
    Warning,
    Error
}

public enum ValidationSource
{
    Metadata,
    Composition
}
