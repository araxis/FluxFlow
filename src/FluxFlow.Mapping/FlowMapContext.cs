namespace FluxFlow.Mapping;

/// <summary>
/// Per-message mapping context passed to mapper functions and expression engines.
/// The input remains the first-class Map argument; Variables carries named values
/// used by host-provided expression engines.
/// </summary>
public sealed record FlowMapContext
{
    private IReadOnlyDictionary<string, object?> _variables =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> Variables
    {
        get => _variables;
        init => _variables = CopyVariables(value);
    }

    private static IReadOnlyDictionary<string, object?> CopyVariables(
        IReadOnlyDictionary<string, object?>? variables)
        => variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(variables, StringComparer.Ordinal);
}
