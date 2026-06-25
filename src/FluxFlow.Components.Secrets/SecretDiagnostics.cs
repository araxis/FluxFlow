using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public static class SecretDiagnostics
{
    public static IReadOnlyList<SecretDiagnostic> ValidateRecords(IEnumerable<SecretRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materialized = records.ToArray();
        var diagnostics = new List<SecretDiagnostic>();

        for (var index = 0; index < materialized.Length; index++)
        {
            var record = materialized[index];
            var path = $"secrets[{index}]";

            if (record.Descriptor is null)
            {
                diagnostics.Add(Invalid(path, "Secret descriptor is required."));
                continue;
            }

            ValidateDescriptor(record.Descriptor, $"{path}.descriptor", diagnostics);

            if (record.Value is null)
            {
                diagnostics.Add(Invalid($"{path}.value", "Secret value is required."));
            }
        }

        diagnostics.AddRange(FindDuplicateSecrets(materialized.Where(record => record.Descriptor is not null)));
        return diagnostics;
    }

    public static IReadOnlyList<SecretDiagnostic> ValidateReference(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var diagnostics = new List<SecretDiagnostic>();
        if (string.IsNullOrWhiteSpace(reference.Name.Value))
            diagnostics.Add(Invalid("reference.name", "Secret reference name is required."));

        ValidateOptionalText(reference.Version, "reference.version", diagnostics);
        ValidateOptionalText(reference.Kind, "reference.kind", diagnostics);
        ValidateMap(reference.Attributes, "reference.attributes", diagnostics);
        return diagnostics;
    }

    public static IReadOnlyList<SecretDiagnostic> ValidateOptionReference(SecretOptionReference option)
    {
        ArgumentNullException.ThrowIfNull(option);

        var diagnostics = new List<SecretDiagnostic>();
        if (string.IsNullOrWhiteSpace(option.OptionPath))
            diagnostics.Add(Invalid("option.path", "Secret option path is required."));

        ValidateMap(option.Metadata, "option.metadata", diagnostics);

        if (option.Reference is null)
        {
            if (option.Required)
            {
                diagnostics.Add(new SecretDiagnostic
                {
                    Code = SecretDiagnosticCode.MissingSecretReference,
                    Severity = SecretDiagnosticSeverity.Error,
                    Message = $"Secret option '{option.OptionPath}' requires a reference.",
                    Metadata = new Dictionary<string, string>
                    {
                        ["path"] = option.OptionPath
                    }
                });
            }

            return diagnostics;
        }

        foreach (var diagnostic in ValidateReference(option.Reference))
        {
            diagnostics.Add(diagnostic with
            {
                Metadata = AddPath(diagnostic.Metadata, option.OptionPath)
            });
        }

        return diagnostics;
    }

    public static void ThrowIfInvalid(IEnumerable<SecretRecord> records)
    {
        var diagnostics = ValidateRecords(records);
        ThrowIfAny(diagnostics, "Secret records are invalid");
    }

    public static void ThrowIfInvalid(SecretReference reference)
    {
        var diagnostics = ValidateReference(reference);
        ThrowIfAny(diagnostics, "Secret reference is invalid");
    }

    public static void ThrowIfInvalid(SecretOptionReference option)
    {
        var diagnostics = ValidateOptionReference(option);
        ThrowIfAny(diagnostics, "Secret option reference is invalid");
    }

    public static IReadOnlyList<SecretDiagnostic> FindDuplicateSecrets(IEnumerable<SecretRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Where(record => record.Descriptor is not null)
            .Where(record => !string.IsNullOrWhiteSpace(record.Descriptor.Name.Value))
            .GroupBy(record => new SecretRecordKey(record.Descriptor.Name, record.Descriptor.Version))
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
    }

    public static async ValueTask<IReadOnlyList<SecretDiagnostic>> FindUnresolvedSecretsAsync(
        ISecretResolver resolver,
        IEnumerable<SecretReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(references);

        var diagnostics = new List<SecretDiagnostic>();
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var referenceDiagnostics = ValidateReference(reference);
            if (referenceDiagnostics.Count > 0)
            {
                diagnostics.AddRange(referenceDiagnostics);
                continue;
            }

            var result = await resolver.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
            if (result.Diagnostic is not null)
                diagnostics.Add(result.Diagnostic);
        }

        return diagnostics;
    }

    private static void ValidateDescriptor(
        SecretDescriptor descriptor,
        string path,
        ICollection<SecretDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Name.Value))
            diagnostics.Add(Invalid($"{path}.name", "Secret name is required."));

        ValidateOptionalText(descriptor.Version, $"{path}.version", diagnostics);
        ValidateOptionalText(descriptor.Kind, $"{path}.kind", diagnostics);
        ValidateOptionalText(descriptor.DisplayName, $"{path}.displayName", diagnostics);
        ValidateOptionalText(descriptor.Summary, $"{path}.summary", diagnostics);
        ValidateMap(descriptor.Metadata, $"{path}.metadata", diagnostics);
    }

    private static void ThrowIfAny(IReadOnlyList<SecretDiagnostic> diagnostics, string prefix)
    {
        if (diagnostics.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        throw new InvalidOperationException($"{prefix}:{Environment.NewLine}{message}");
    }

    private static SecretDiagnostic Invalid(string path, string message)
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

    private static void ValidateOptionalText(
        string? value,
        string path,
        ICollection<SecretDiagnostic> diagnostics)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            diagnostics.Add(Invalid(path, "Value cannot be empty when it is provided."));
    }

    private static void ValidateMap(
        IReadOnlyDictionary<string, string>? values,
        string path,
        ICollection<SecretDiagnostic> diagnostics)
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

        foreach (var key in SecretContractMap.FindDuplicateNormalizedKeys(values))
        {
            diagnostics.Add(Invalid(path, $"Key '{key}' is declared more than once after trimming."));
        }
    }

    private static IReadOnlyDictionary<string, string> AddPath(
        IReadOnlyDictionary<string, string>? metadata,
        string optionPath)
    {
        var values = metadata?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (values.TryGetValue("path", out var referencePath))
            values["referencePath"] = referencePath;

        values["path"] = optionPath;
        values["optionPath"] = optionPath;
        return values;
    }

    private readonly record struct SecretRecordKey(SecretName Name, string? Version);
}
