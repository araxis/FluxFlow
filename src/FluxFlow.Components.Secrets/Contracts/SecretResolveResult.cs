namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretResolveResult
{
    public required SecretReference Reference { get; init; }
    public SecretDescriptor? Descriptor { get; init; }
    public SecretValue? Value { get; init; }
    public SecretDiagnostic? Diagnostic { get; init; }
    public bool Resolved => Value is not null && Diagnostic is null;

    public override string ToString()
        => Diagnostic is null
            ? $"Resolved secret '{Reference}'."
            : Diagnostic.ToString();

    public static SecretResolveResult ResolvedResult(
        SecretReference reference,
        SecretDescriptor descriptor,
        SecretValue value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(value);

        return new()
        {
            Reference = reference,
            Descriptor = descriptor,
            Value = value
        };
    }

    public static SecretResolveResult Missing(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new()
        {
            Reference = reference,
            Diagnostic = new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.MissingSecret,
                Severity = SecretDiagnosticSeverity.Error,
                Message = $"Secret '{reference}' was not found.",
                Name = reference.Name,
                Version = reference.Version,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }

    public static SecretResolveResult Ambiguous(
        SecretReference reference,
        IReadOnlyCollection<SecretDescriptor> matches)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(matches);

        return new()
        {
            Reference = reference,
            Diagnostic = new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.AmbiguousSecret,
                Severity = SecretDiagnosticSeverity.Error,
                Message = $"Secret '{reference.Name}' matched {matches.Count} versions. Provide a version.",
                Name = reference.Name,
                Kind = reference.Kind,
                Reference = reference,
                Metadata = new Dictionary<string, string>
                {
                    ["matchCount"] = matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            }
        };
    }

    public static SecretResolveResult KindMismatch(SecretReference reference, SecretDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new()
        {
            Reference = reference,
            Descriptor = descriptor,
            Diagnostic = new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.KindMismatch,
                Severity = SecretDiagnosticSeverity.Error,
                Message = $"Secret '{reference}' has kind '{descriptor.Kind}', but kind '{reference.Kind}' was requested.",
                Name = reference.Name,
                Version = reference.Version,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }

    public static SecretResolveResult AccessDenied(SecretReference reference, string message)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new()
        {
            Reference = reference,
            Diagnostic = new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.AccessDenied,
                Severity = SecretDiagnosticSeverity.Error,
                Message = message,
                Name = reference.Name,
                Version = reference.Version,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }

    public static SecretResolveResult Failed(SecretReference reference, string message)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new()
        {
            Reference = reference,
            Diagnostic = new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.ResolveFailed,
                Severity = SecretDiagnosticSeverity.Error,
                Message = message,
                Name = reference.Name,
                Version = reference.Version,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }
}
