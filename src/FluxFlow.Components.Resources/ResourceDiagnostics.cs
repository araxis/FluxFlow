using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Resources;

public static class ResourceDiagnostics
{
    public static IReadOnlyList<ResourceDiagnostic> ValidateDescriptors(IEnumerable<ResourceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var materialized = descriptors.ToArray();
        var diagnostics = new List<ResourceDiagnostic>();

        for (var index = 0; index < materialized.Length; index++)
        {
            var descriptor = materialized[index];
            var path = $"resources[{index}]";

            if (descriptor is null)
            {
                diagnostics.Add(Invalid(path, "Resource descriptor is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Name.Value))
            {
                diagnostics.Add(Invalid(path, "Resource name is required."));
            }

            ValidateOptionalText(descriptor.Kind, $"{path}.kind", diagnostics);
            ValidateOptionalText(descriptor.DisplayName, $"{path}.displayName", diagnostics);
            ValidateOptionalText(descriptor.Summary, $"{path}.summary", diagnostics);
            ValidateMap(descriptor.Metadata, $"{path}.metadata", diagnostics);
        }

        diagnostics.AddRange(FindDuplicateResources(materialized.OfType<ResourceDescriptor>()));
        return diagnostics;
    }

    public static IReadOnlyList<ResourceDiagnostic> ValidateReference(ResourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var diagnostics = new List<ResourceDiagnostic>();
        if (string.IsNullOrWhiteSpace(reference.Name.Value))
            diagnostics.Add(Invalid("reference.name", "Resource reference name is required."));

        ValidateOptionalText(reference.Kind, "reference.kind", diagnostics);
        ValidateMap(reference.Attributes, "reference.attributes", diagnostics);
        return diagnostics;
    }

    public static void ThrowIfInvalid(IEnumerable<ResourceDescriptor> descriptors)
    {
        var diagnostics = ValidateDescriptors(descriptors);
        ThrowIfAny(diagnostics, "Resource descriptors are invalid");
    }

    public static void ThrowIfInvalid(ResourceReference reference)
    {
        var diagnostics = ValidateReference(reference);
        ThrowIfAny(diagnostics, "Resource reference is invalid");
    }

    public static IReadOnlyList<ResourceDiagnostic> FindDuplicateResources(IEnumerable<ResourceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        return descriptors
            .OfType<ResourceDescriptor>()
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name.Value))
            .GroupBy(descriptor => descriptor.Name, descriptor => descriptor, EqualityComparer<ResourceName>.Default)
            .Where(group => group.Count() > 1)
            .Select(group => new ResourceDiagnostic
            {
                Code = ResourceDiagnosticCode.DuplicateResource,
                Severity = ResourceDiagnosticSeverity.Error,
                Message = $"Resource '{group.Key}' is declared more than once.",
                Name = group.Key
            })
            .ToArray();
    }

    public static async ValueTask<IReadOnlyList<ResourceDiagnostic>> FindMissingResourcesAsync(
        IResourceLookup lookup,
        IEnumerable<ResourceReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(references);

        var diagnostics = new List<ResourceDiagnostic>();
        var index = 0;
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference is null)
            {
                diagnostics.Add(Invalid($"references[{index}]", "Resource reference is required."));
                index++;
                continue;
            }

            var referenceDiagnostics = ValidateReference(reference);
            if (referenceDiagnostics.Count > 0)
            {
                diagnostics.AddRange(referenceDiagnostics);
                index++;
                continue;
            }

            var result = await lookup.LookupAsync(reference, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is
                {
                    Code: ResourceDiagnosticCode.MissingResource or ResourceDiagnosticCode.KindMismatch
                } diagnostic)
            {
                diagnostics.Add(diagnostic);
            }

            index++;
        }

        return diagnostics;
    }

    public static IReadOnlyList<ResourceDiagnostic> FindUnusedResources(
        IEnumerable<ResourceDescriptor> descriptors,
        IEnumerable<ResourceReference> references)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(references);

        var used = references
            .OfType<ResourceReference>()
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Name.Value))
            .Select(reference => reference.Name)
            .ToHashSet();

        return descriptors
            .OfType<ResourceDescriptor>()
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name.Value))
            .Where(descriptor => !used.Contains(descriptor.Name))
            .Select(descriptor => new ResourceDiagnostic
            {
                Code = ResourceDiagnosticCode.UnusedResource,
                Severity = ResourceDiagnosticSeverity.Information,
                Message = $"Resource '{descriptor.Name}' is declared but not referenced.",
                Name = descriptor.Name,
                Kind = descriptor.Kind
            })
            .ToArray();
    }

    private static void ThrowIfAny(IReadOnlyList<ResourceDiagnostic> diagnostics, string prefix)
    {
        if (diagnostics.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        throw new InvalidOperationException($"{prefix}:{Environment.NewLine}{message}");
    }

    private static ResourceDiagnostic Invalid(string path, string message)
        => new()
        {
            Code = ResourceDiagnosticCode.InvalidResource,
            Severity = ResourceDiagnosticSeverity.Error,
            Message = $"{path}: {message}",
            Metadata = new Dictionary<string, string>
            {
                ["path"] = path
            }
        };

    private static void ValidateOptionalText(
        string? value,
        string path,
        ICollection<ResourceDiagnostic> diagnostics)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            diagnostics.Add(Invalid(path, "Value cannot be empty when it is provided."));
    }

    private static void ValidateMap(
        IReadOnlyDictionary<string, string>? values,
        string path,
        ICollection<ResourceDiagnostic> diagnostics)
    {
        if (values is null)
        {
            diagnostics.Add(Invalid(path, "Map cannot be null."));
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
                diagnostics.Add(Invalid(path, "Keys are required."));

            if (string.IsNullOrWhiteSpace(value.Value))
                diagnostics.Add(Invalid($"{path}.{value.Key}", "Values are required."));
        }

        foreach (var key in ResourceContractMap.FindDuplicateNormalizedKeys(values))
        {
            diagnostics.Add(Invalid(path, $"Key '{key}' is declared more than once after trimming."));
        }
    }
}
