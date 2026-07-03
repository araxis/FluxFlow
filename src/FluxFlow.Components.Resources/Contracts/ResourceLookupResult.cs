namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceLookupResult
{
    public required ResourceReference Reference { get; init; }
    public ResourceDescriptor? Descriptor { get; init; }
    public ResourceDiagnostic? Diagnostic { get; init; }
    public bool Found => Descriptor is not null && Diagnostic is null;

    public static ResourceLookupResult FoundResult(ResourceReference reference, ResourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new()
        {
            Reference = reference,
            Descriptor = descriptor
        };
    }

    public static ResourceLookupResult Missing(ResourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new()
        {
            Reference = reference,
            Diagnostic = new ResourceDiagnostic
            {
                Code = ResourceDiagnosticCode.MissingResource,
                Severity = ResourceDiagnosticSeverity.Error,
                Message = $"Resource '{reference.Name}' was not found.",
                Name = reference.Name,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }

    public static ResourceLookupResult KindMismatch(ResourceReference reference, ResourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new()
        {
            Reference = reference,
            Descriptor = descriptor,
            Diagnostic = new ResourceDiagnostic
            {
                Code = ResourceDiagnosticCode.KindMismatch,
                Severity = ResourceDiagnosticSeverity.Error,
                Message = $"Resource '{reference.Name}' has kind '{descriptor.Kind}', but kind '{reference.Kind}' was requested.",
                Name = reference.Name,
                Kind = reference.Kind,
                Reference = reference
            }
        };
    }
}
