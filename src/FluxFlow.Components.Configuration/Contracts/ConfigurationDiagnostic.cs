namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationDiagnostic
{
    private string _code = string.Empty;
    private string _message = string.Empty;
    private string? _path;
    private string? _name;
    private string? _kind;
    private IReadOnlyDictionary<string, string> _metadata = new Dictionary<string, string>(StringComparer.Ordinal);

    public required ConfigurationDiagnosticSource Source { get; init; }
    public required string Code
    {
        get => _code;
        init => _code = ConfigurationContractMap.NormalizeRequired(value);
    }

    public required ConfigurationDiagnosticSeverity Severity { get; init; }
    public required string Message
    {
        get => _message;
        init => _message = ConfigurationContractMap.NormalizeRequired(value);
    }

    public string? Path
    {
        get => _path;
        init => _path = ConfigurationContractMap.NormalizeOptional(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = ConfigurationContractMap.NormalizeOptional(value);
    }

    public string? Kind
    {
        get => _kind;
        init => _kind = ConfigurationContractMap.NormalizeOptional(value);
    }

    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata;
        init => _metadata = ConfigurationContractMap.Copy(value);
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Path)
            ? $"{Severity} {Source}.{Code}: {Message}"
            : $"{Severity} {Source}.{Code} at {Path}: {Message}";
}
