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
        diagnostics.AddRange(await ValidateResourcesAsync(resourceLookup, request.Resources, cancellationToken).ConfigureAwait(false));
        diagnostics.AddRange(await ValidateSecretsAsync(secretResolver, request.Secrets, cancellationToken).ConfigureAwait(false));

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
        foreach (var option in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var optionDiagnostics = ValidateResourceOption(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics);
                continue;
            }

            if (option.Reference is null)
                continue;

            var referenceDiagnostics = ResourceDiagnostics
                .ValidateReference(option.Reference)
                .Select(diagnostic => FromResourceDiagnostic(diagnostic, option.Path))
                .ToArray();

            if (referenceDiagnostics.Length > 0)
            {
                diagnostics.AddRange(referenceDiagnostics);
                continue;
            }

            var result = await lookup.LookupAsync(option.Reference, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is not null)
                diagnostics.Add(FromResourceDiagnostic(result.Diagnostic, option.Path));
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
        foreach (var option in options)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var optionDiagnostics = SecretDiagnostics.ValidateOptionReference(option);
            if (optionDiagnostics.Count > 0)
            {
                diagnostics.AddRange(optionDiagnostics.Select(diagnostic => FromSecretDiagnostic(diagnostic, option.OptionPath)));
                continue;
            }

            var result = await SecretOptionResolver.ResolveAsync(resolver, option, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is not null)
                diagnostics.Add(FromSecretDiagnostic(result.Diagnostic, result.OptionPath));
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
}
