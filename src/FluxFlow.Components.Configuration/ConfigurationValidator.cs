using FluxFlow.Components.Configuration.Contracts;
using FluxFlow.Components.Resources;
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets;
using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Configuration;

public static class ConfigurationValidator
{
    public static async ValueTask<ConfigurationValidationReport> ValidateAsync(
        IResourceLookup resourceLookup,
        ISecretResolver secretResolver,
        ConfigurationValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceLookup);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ConfigurationDiagnostic>();
        if (request.Resources is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "resources",
                "request.resources",
                "Configuration validation resources collection cannot be null."));
        }
        else
        {
            diagnostics.AddRange(await ValidateResourcesAsync(resourceLookup, request.Resources, cancellationToken).ConfigureAwait(false));
        }

        if (request.Secrets is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "secrets",
                "request.secrets",
                "Configuration validation secrets collection cannot be null."));
        }
        else
        {
            diagnostics.AddRange(await ValidateSecretsAsync(secretResolver, request.Secrets, cancellationToken).ConfigureAwait(false));
        }

        return ConfigurationValidationReport.FromDiagnostics(diagnostics);
    }

    public static ConfigurationValidationReport ValidateDeclaredReferences(
        IResourceDescriptorProvider resourceDescriptorProvider,
        ISecretDescriptorProvider secretDescriptorProvider,
        ConfigurationValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(resourceDescriptorProvider);
        ArgumentNullException.ThrowIfNull(secretDescriptorProvider);
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ConfigurationDiagnostic>();
        if (request.Resources is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "resources",
                "request.resources",
                "Configuration validation resources collection cannot be null."));
        }
        else
        {
            diagnostics.AddRange(ValidateDeclaredResources(resourceDescriptorProvider, request.Resources));
        }

        if (request.Secrets is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "secrets",
                "request.secrets",
                "Configuration validation secrets collection cannot be null."));
        }
        else
        {
            diagnostics.AddRange(ValidateDeclaredSecrets(secretDescriptorProvider, request.Secrets));
        }

        return ConfigurationValidationReport.FromDiagnostics(diagnostics);
    }

    public static async ValueTask<IReadOnlyList<ConfigurationDiagnostic>> ValidateResourcesAsync(
        IResourceLookup lookup,
        IEnumerable<ConfigurationResourceReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(references);

        var diagnostics = new List<ConfigurationDiagnostic>();
        var index = 0;
        foreach (var option in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (option is null)
            {
                diagnostics.Add(InvalidValidationRequest(
                    $"resources[{index}]",
                    $"resources[{index}]",
                    $"Resource validation reference at index {index} cannot be null."));
                index++;
                continue;
            }

            var optionDiagnostics = ValidateResourceOption(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics);
                index++;
                continue;
            }

            if (option.Reference is null)
            {
                index++;
                continue;
            }

            var referenceDiagnostics = ResourceDiagnostics
                .ValidateReference(option.Reference)
                .Select(diagnostic => FromResourceDiagnostic(diagnostic, option.Path))
                .ToArray();

            if (referenceDiagnostics.Length > 0)
            {
                diagnostics.AddRange(referenceDiagnostics);
                index++;
                continue;
            }

            var result = await lookup.LookupAsync(option.Reference, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is not null)
                diagnostics.Add(FromResourceDiagnostic(result.Diagnostic, option.Path));

            index++;
        }

        return diagnostics;
    }

    public static IReadOnlyList<ConfigurationDiagnostic> ValidateDeclaredResources(
        IResourceDescriptorProvider descriptorProvider,
        IEnumerable<ConfigurationResourceReference> references)
    {
        ArgumentNullException.ThrowIfNull(descriptorProvider);
        ArgumentNullException.ThrowIfNull(references);

        var diagnostics = new List<ConfigurationDiagnostic>();
        var descriptors = descriptorProvider.GetResources();
        if (descriptors is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "resources",
                "resourceDescriptors",
                "Resource descriptor provider returned a null collection."));
            return diagnostics;
        }

        diagnostics.AddRange(ResourceDiagnostics.ValidateDescriptors(descriptors)
            .Select(diagnostic => FromResourceDiagnostic(
                diagnostic,
                DiagnosticPath(diagnostic.Metadata, "resourceDescriptors"))));

        var validDescriptors = descriptors
            .OfType<ResourceDescriptor>()
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name.Value))
            .ToArray();
        var index = 0;
        foreach (var option in references)
        {
            if (option is null)
            {
                diagnostics.Add(InvalidValidationRequest(
                    $"resources[{index}]",
                    $"resources[{index}]",
                    $"Resource validation reference at index {index} cannot be null."));
                index++;
                continue;
            }

            var optionDiagnostics = ValidateResourceOption(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics);
                index++;
                continue;
            }

            if (option.Reference is null)
            {
                index++;
                continue;
            }

            var referenceDiagnostics = ResourceDiagnostics
                .ValidateReference(option.Reference)
                .Select(diagnostic => FromResourceDiagnostic(diagnostic, option.Path))
                .ToArray();

            if (referenceDiagnostics.Length > 0)
            {
                diagnostics.AddRange(referenceDiagnostics);
                index++;
                continue;
            }

            var declarationDiagnostic = FindDeclaredResourceDiagnostic(validDescriptors, option.Reference);
            if (declarationDiagnostic is not null)
                diagnostics.Add(FromResourceDiagnostic(declarationDiagnostic, option.Path));

            index++;
        }

        return diagnostics;
    }

    public static async ValueTask<IReadOnlyList<ConfigurationDiagnostic>> ValidateSecretsAsync(
        ISecretResolver resolver,
        IEnumerable<SecretOptionReference> options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ConfigurationDiagnostic>();
        var index = 0;
        foreach (var option in options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (option is null)
            {
                diagnostics.Add(InvalidValidationRequest(
                    $"secrets[{index}]",
                    $"secrets[{index}]",
                    $"Secret validation reference at index {index} cannot be null."));
                index++;
                continue;
            }

            var optionDiagnostics = SecretDiagnostics.ValidateOptionReference(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics.Select(diagnostic => FromSecretDiagnostic(diagnostic, option.OptionPath)));
                index++;
                continue;
            }

            var result = await SecretOptionResolver.ResolveAsync(resolver, option, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is not null)
                diagnostics.Add(FromSecretDiagnostic(result.Diagnostic, result.OptionPath));

            index++;
        }

        return diagnostics;
    }

    public static IReadOnlyList<ConfigurationDiagnostic> ValidateDeclaredSecrets(
        ISecretDescriptorProvider descriptorProvider,
        IEnumerable<SecretOptionReference> options)
    {
        ArgumentNullException.ThrowIfNull(descriptorProvider);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ConfigurationDiagnostic>();
        var descriptors = descriptorProvider.GetDescriptors();
        if (descriptors is null)
        {
            diagnostics.Add(InvalidValidationRequest(
                "secrets",
                "secretDescriptors",
                "Secret descriptor provider returned a null collection."));
            return diagnostics;
        }

        diagnostics.AddRange(ValidateSecretDescriptors(descriptors)
            .Select(diagnostic => FromSecretDiagnostic(
                diagnostic,
                DiagnosticPath(diagnostic.Metadata, "secretDescriptors"))));

        var validDescriptors = descriptors
            .OfType<SecretDescriptor>()
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name.Value))
            .ToArray();
        var index = 0;
        foreach (var option in options)
        {
            if (option is null)
            {
                diagnostics.Add(InvalidValidationRequest(
                    $"secrets[{index}]",
                    $"secrets[{index}]",
                    $"Secret validation reference at index {index} cannot be null."));
                index++;
                continue;
            }

            var optionDiagnostics = SecretDiagnostics.ValidateOptionReference(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics.Select(diagnostic => FromSecretDiagnostic(diagnostic, option.OptionPath)));
                index++;
                continue;
            }

            if (option.Reference is null)
            {
                index++;
                continue;
            }

            var declarationDiagnostic = FindDeclaredSecretDiagnostic(validDescriptors, option.Reference);
            if (declarationDiagnostic is not null)
                diagnostics.Add(FromSecretDiagnostic(declarationDiagnostic, option.OptionPath));

            index++;
        }

        return diagnostics;
    }

    private static IReadOnlyList<ConfigurationDiagnostic> ValidateResourceOption(ConfigurationResourceReference option)
    {
        ArgumentNullException.ThrowIfNull(option);

        var diagnostics = new List<ConfigurationDiagnostic>();
        if (string.IsNullOrWhiteSpace(option.Path))
        {
            diagnostics.Add(new ConfigurationDiagnostic
            {
                Source = ConfigurationDiagnosticSource.Configuration,
                Code = "InvalidResourceReference",
                Severity = ConfigurationDiagnosticSeverity.Error,
                Message = "Resource reference path is required.",
                Metadata = new Dictionary<string, string>
                {
                    ["referencePath"] = "resource.path"
                }
            });
        }

        if (option.Reference is null && option.Required)
        {
            diagnostics.Add(new ConfigurationDiagnostic
            {
                Source = ConfigurationDiagnosticSource.Resource,
                Code = "MissingResourceReference",
                Severity = ConfigurationDiagnosticSeverity.Error,
                Message = $"Resource option '{option.Path}' requires a reference.",
                Path = option.Path,
                Metadata = Merge(option.Metadata, option.Path)
            });
        }

        ValidateMetadata(option.Metadata, option.Path, diagnostics);
        return diagnostics;
    }

    private static ResourceDiagnostic? FindDeclaredResourceDiagnostic(
        IEnumerable<ResourceDescriptor> descriptors,
        ResourceReference reference)
    {
        var matches = descriptors
            .Where(descriptor => descriptor.Name == reference.Name)
            .ToArray();

        if (matches.Length == 0)
            return ResourceLookupResult.Missing(reference).Diagnostic;

        if (!string.IsNullOrWhiteSpace(reference.Kind)
            && !matches.Any(descriptor => string.Equals(descriptor.Kind, reference.Kind, StringComparison.Ordinal)))
        {
            return ResourceLookupResult.KindMismatch(reference, matches[0]).Diagnostic;
        }

        return null;
    }

    private static SecretDiagnostic? FindDeclaredSecretDiagnostic(
        IEnumerable<SecretDescriptor> descriptors,
        SecretReference reference)
    {
        var matches = descriptors
            .Where(descriptor => descriptor.Name == reference.Name)
            .Where(descriptor => string.IsNullOrWhiteSpace(reference.Version)
                || string.Equals(descriptor.Version, reference.Version, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 0)
            return SecretResolveResult.Missing(reference).Diagnostic;

        if (!string.IsNullOrWhiteSpace(reference.Kind))
        {
            var kindMatches = matches
                .Where(descriptor => string.Equals(descriptor.Kind, reference.Kind, StringComparison.Ordinal))
                .ToArray();

            if (kindMatches.Length == 0)
                return SecretResolveResult.KindMismatch(reference, matches[0]).Diagnostic;

            matches = kindMatches;
        }

        if (string.IsNullOrWhiteSpace(reference.Version) && matches.Length > 1)
            return SecretResolveResult.Ambiguous(reference, matches).Diagnostic;

        return null;
    }

    private static IReadOnlyList<SecretDiagnostic> ValidateSecretDescriptors(IEnumerable<SecretDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var materialized = descriptors.ToArray();
        var diagnostics = new List<SecretDiagnostic>();

        for (var index = 0; index < materialized.Length; index++)
        {
            var descriptor = materialized[index];
            var path = $"secrets[{index}]";

            if (descriptor is null)
            {
                diagnostics.Add(InvalidSecretDescriptor(path, "Secret descriptor is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(descriptor.Name.Value))
                diagnostics.Add(InvalidSecretDescriptor($"{path}.name", "Secret name is required."));

            ValidateOptionalText(descriptor.Version, $"{path}.version", diagnostics);
            ValidateOptionalText(descriptor.Kind, $"{path}.kind", diagnostics);
            ValidateOptionalText(descriptor.DisplayName, $"{path}.displayName", diagnostics);
            ValidateOptionalText(descriptor.Summary, $"{path}.summary", diagnostics);
            ValidateMap(descriptor.Metadata, $"{path}.metadata", diagnostics);
        }

        diagnostics.AddRange(FindDuplicateSecretDescriptors(materialized.OfType<SecretDescriptor>()));
        return diagnostics;
    }

    private static IReadOnlyList<SecretDiagnostic> FindDuplicateSecretDescriptors(IEnumerable<SecretDescriptor> descriptors)
        => descriptors
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Name.Value))
            .GroupBy(descriptor => new SecretDescriptorKey(descriptor.Name, descriptor.Version))
            .Where(group => group.Count() > 1)
            .Select(group => new SecretDiagnostic
            {
                Code = SecretDiagnosticCode.DuplicateSecret,
                Severity = SecretDiagnosticSeverity.Error,
                Message = string.IsNullOrWhiteSpace(group.Key.Version)
                    ? $"Secret '{group.Key.Name}' is declared more than once."
                    : $"Secret '{group.Key.Name}@{group.Key.Version}' is declared more than once.",
                Name = group.Key.Name,
                Version = group.Key.Version
            })
            .ToArray();

    private static void ValidateOptionalText(
        string? value,
        string path,
        ICollection<SecretDiagnostic> diagnostics)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            diagnostics.Add(InvalidSecretDescriptor(path, "Value cannot be empty when it is provided."));
    }

    private static void ValidateMap(
        IReadOnlyDictionary<string, string>? values,
        string path,
        ICollection<SecretDiagnostic> diagnostics)
    {
        if (values is null)
        {
            diagnostics.Add(InvalidSecretDescriptor(path, "Map cannot be null."));
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
                diagnostics.Add(InvalidSecretDescriptor(path, "Keys are required."));

            if (string.IsNullOrWhiteSpace(value.Value))
                diagnostics.Add(InvalidSecretDescriptor($"{path}.{value.Key}", "Values are required."));
        }

        foreach (var key in ConfigurationContractMap.FindDuplicateNormalizedKeys(values))
            diagnostics.Add(InvalidSecretDescriptor(path, $"Key '{key}' is declared more than once after trimming."));
    }

    private static SecretDiagnostic InvalidSecretDescriptor(string path, string message)
        => new()
        {
            Code = SecretDiagnosticCode.InvalidSecret,
            Severity = SecretDiagnosticSeverity.Error,
            Message = $"{path}: {message}",
            Metadata = new Dictionary<string, string>
            {
                ["path"] = path
            }
        };

    private static ConfigurationDiagnostic FromResourceDiagnostic(ResourceDiagnostic diagnostic, string path)
        => new()
        {
            Source = ConfigurationDiagnosticSource.Resource,
            Code = diagnostic.Code.ToString(),
            Severity = diagnostic.Severity switch
            {
                ResourceDiagnosticSeverity.Warning => ConfigurationDiagnosticSeverity.Warning,
                ResourceDiagnosticSeverity.Error => ConfigurationDiagnosticSeverity.Error,
                _ => ConfigurationDiagnosticSeverity.Information
            },
            Message = diagnostic.Message,
            Path = path,
            Name = diagnostic.Name?.ToString(),
            Kind = diagnostic.Kind,
            Metadata = Merge(diagnostic.Metadata, path)
        };

    private static ConfigurationDiagnostic FromSecretDiagnostic(SecretDiagnostic diagnostic, string path)
        => new()
        {
            Source = ConfigurationDiagnosticSource.Secret,
            Code = diagnostic.Code.ToString(),
            Severity = diagnostic.Severity switch
            {
                SecretDiagnosticSeverity.Warning => ConfigurationDiagnosticSeverity.Warning,
                SecretDiagnosticSeverity.Error => ConfigurationDiagnosticSeverity.Error,
                _ => ConfigurationDiagnosticSeverity.Information
            },
            Message = diagnostic.Message,
            Path = path,
            Name = diagnostic.Name?.ToString(),
            Kind = diagnostic.Kind,
            Metadata = Merge(diagnostic.Metadata, path)
        };

    private static string DiagnosticPath(
        IReadOnlyDictionary<string, string>? metadata,
        string fallback)
        => metadata is not null
            && metadata.TryGetValue("path", out var path)
            && !string.IsNullOrWhiteSpace(path)
                ? path
                : fallback;

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string>? metadata,
        string? path)
    {
        var values = metadata?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (values.TryGetValue("path", out var referencePath) && !values.ContainsKey("referencePath"))
            values["referencePath"] = referencePath;

        if (!string.IsNullOrWhiteSpace(path))
        {
            values["path"] = path;
            values["optionPath"] = path;
        }

        return values;
    }

    private static void ValidateMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        string? optionPath,
        ICollection<ConfigurationDiagnostic> diagnostics)
    {
        if (metadata is null)
        {
            diagnostics.Add(InvalidResourceOptionMetadata(
                optionPath,
                "resource.metadata",
                "Resource option metadata cannot be null."));
            return;
        }

        foreach (var value in metadata)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
            {
                diagnostics.Add(InvalidResourceOptionMetadata(
                    optionPath,
                    "resource.metadata",
                    "Resource option metadata keys are required."));
            }

            if (string.IsNullOrWhiteSpace(value.Value))
            {
                diagnostics.Add(InvalidResourceOptionMetadata(
                    optionPath,
                    $"resource.metadata.{value.Key}",
                    "Resource option metadata values are required."));
            }
        }

        foreach (var key in ConfigurationContractMap.FindDuplicateNormalizedKeys(metadata))
        {
            diagnostics.Add(InvalidResourceOptionMetadata(
                optionPath,
                "resource.metadata",
                $"Resource option metadata key '{key}' is declared more than once after trimming."));
        }
    }

    private static ConfigurationDiagnostic InvalidResourceOptionMetadata(
        string? optionPath,
        string referencePath,
        string message)
        => new()
        {
            Source = ConfigurationDiagnosticSource.Configuration,
            Code = "InvalidResourceReference",
            Severity = ConfigurationDiagnosticSeverity.Error,
            Message = message,
            Path = optionPath,
            Metadata = Merge(new Dictionary<string, string>
            {
                ["referencePath"] = referencePath
            }, optionPath)
        };

    private static ConfigurationDiagnostic InvalidValidationRequest(
        string path,
        string referencePath,
        string message)
        => new()
        {
            Source = ConfigurationDiagnosticSource.Configuration,
            Code = "InvalidConfigurationValidationRequest",
            Severity = ConfigurationDiagnosticSeverity.Error,
            Message = message,
            Path = path,
            Metadata = Merge(new Dictionary<string, string>
            {
                ["referencePath"] = referencePath
            }, path)
        };

    private readonly record struct SecretDescriptorKey(SecretName Name, string? Version);
}
