namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationValidationReport
{
    private IReadOnlyList<ConfigurationDiagnostic> _diagnostics = [];

    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = value is null
            ? []
            : SnapshotDiagnostics(value, nameof(Diagnostics));
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
            Diagnostics = SnapshotDiagnostics(diagnostics, nameof(diagnostics))
        };
    }

    private static IReadOnlyList<ConfigurationDiagnostic> SnapshotDiagnostics(
        IEnumerable<ConfigurationDiagnostic> diagnostics,
        string argumentName)
    {
        var snapshot = diagnostics.ToArray();

        if (Array.Exists(snapshot, diagnostic => diagnostic is null))
        {
            throw new ArgumentNullException(
                argumentName,
                "Configuration validation reports cannot contain null diagnostics.");
        }

        return snapshot;
    }
}
