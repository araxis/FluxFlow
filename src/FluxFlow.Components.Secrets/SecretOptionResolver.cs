using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public static class SecretOptionResolver
{
    public static ValueTask<SecretOptionResolution> ResolveRequiredAsync(
        ISecretResolver resolver,
        SecretReference? reference,
        string optionPath,
        CancellationToken cancellationToken = default)
        => ResolveAsync(
            resolver,
            new SecretOptionReference
            {
                OptionPath = optionPath,
                Reference = reference,
                Required = true
            },
            cancellationToken);

    public static ValueTask<SecretOptionResolution> ResolveOptionalAsync(
        ISecretResolver resolver,
        SecretReference? reference,
        string optionPath,
        CancellationToken cancellationToken = default)
        => ResolveAsync(
            resolver,
            new SecretOptionReference
            {
                OptionPath = optionPath,
                Reference = reference,
                Required = false
            },
            cancellationToken);

    public static async ValueTask<SecretOptionResolution> ResolveAsync(
        ISecretResolver resolver,
        SecretOptionReference option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(option);

        var diagnostics = SecretDiagnostics.ValidateOptionReference(option);
        if (diagnostics.Count > 0)
            return SecretOptionResolution.Failed(option, diagnostics[0]);

        if (option.Reference is null)
            return SecretOptionResolution.NotProvidedResult(option);

        var result = await resolver.ResolveAsync(option.Reference, cancellationToken).ConfigureAwait(false);
        return SecretOptionResolution.FromResult(option, result);
    }

    public static async ValueTask<IReadOnlyList<SecretOptionResolution>> ResolveAllAsync(
        ISecretResolver resolver,
        IEnumerable<SecretOptionReference> options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<SecretOptionResolution>();
        var index = 0;
        foreach (var option in options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (option is null)
            {
                var path = $"options[{index}]";
                results.Add(SecretOptionResolution.Failed(
                    new SecretOptionReference
                    {
                        OptionPath = path,
                        Required = false
                    },
                    InvalidOptionEntry(path)));
                index++;
                continue;
            }

            results.Add(await ResolveAsync(resolver, option, cancellationToken).ConfigureAwait(false));
            index++;
        }

        return results;
    }

    private static SecretDiagnostic InvalidOptionEntry(string path)
        => new()
        {
            Code = SecretDiagnosticCode.InvalidSecret,
            Severity = SecretDiagnosticSeverity.Error,
            Message = $"{path}: Secret option reference is required.",
            Metadata = new Dictionary<string, string>
            {
                ["path"] = path
            }
        };
}
