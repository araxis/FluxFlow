namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationValidationReport
{
    private IReadOnlyList<ConfigurationDiagnostic> _diagnostics = [];

    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = value?.ToArray() ?? [];
    }

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == ConfigurationDiagnosticSeverity.Error);
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == ConfigurationDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == ConfigurationDiagnosticSeverity.Warning);

    public static ConfigurationValidationReport Empty { get; } = new();

    public static ConfigurationValidationReport FromDiagnostics(IEnumerable<ConfigurationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new ConfigurationValidationReport
        {
            Diagnostics = diagnostics.ToArray()
        };
    }
}
