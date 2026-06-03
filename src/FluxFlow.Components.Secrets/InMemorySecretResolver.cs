using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public sealed class InMemorySecretResolver : ISecretResolver
{
    private readonly IReadOnlyList<SecretRecord> _records;

    public InMemorySecretResolver(IEnumerable<SecretRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        _records = records.ToArray();
        SecretDiagnostics.ThrowIfInvalid(_records);
    }

    public ValueTask<SecretResolveResult> ResolveAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecretDiagnostics.ThrowIfInvalid(reference);

        var matches = _records
            .Where(record => record.Descriptor.Name == reference.Name)
            .Where(record => string.IsNullOrWhiteSpace(reference.Version)
                || string.Equals(record.Descriptor.Version, reference.Version, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 0)
            return ValueTask.FromResult(SecretResolveResult.Missing(reference));

        if (!string.IsNullOrWhiteSpace(reference.Kind))
        {
            var kindMatches = matches
                .Where(record => string.Equals(record.Descriptor.Kind, reference.Kind, StringComparison.Ordinal))
                .ToArray();

            if (kindMatches.Length == 0)
                return ValueTask.FromResult(SecretResolveResult.KindMismatch(reference, matches[0].Descriptor));

            matches = kindMatches;
        }

        if (string.IsNullOrWhiteSpace(reference.Version) && matches.Length > 1)
            return ValueTask.FromResult(SecretResolveResult.Ambiguous(reference, matches.Select(match => match.Descriptor).ToArray()));

        var record = matches[0];
        return ValueTask.FromResult(SecretResolveResult.ResolvedResult(reference, record.Descriptor, record.Value));
    }

    public IReadOnlyCollection<SecretDescriptor> GetDescriptors()
        => _records.Select(record => record.Descriptor).ToArray();
}
