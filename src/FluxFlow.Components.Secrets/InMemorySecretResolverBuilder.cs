using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public sealed class InMemorySecretResolverBuilder
{
    private readonly List<SecretRecord> records = [];

    public InMemorySecretResolverBuilder Add(SecretRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        records.Add(record);
        return this;
    }

    public InMemorySecretResolverBuilder AddRange(IEnumerable<SecretRecord> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        foreach (var record in secrets)
            Add(record);

        return this;
    }

    public InMemorySecretResolverBuilder Add(
        string name,
        string value,
        string? version = null,
        string? kind = null,
        string? displayName = null,
        string? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        return Add(
            name,
            new SecretValue(value),
            version,
            kind,
            displayName,
            summary,
            metadata);
    }

    public InMemorySecretResolverBuilder Add(
        string name,
        SecretValue value,
        string? version = null,
        string? kind = null,
        string? displayName = null,
        string? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var descriptor = new SecretDescriptor
        {
            Name = new SecretName(name),
            Version = version,
            Kind = kind,
            DisplayName = displayName,
            Summary = summary
        };

        if (metadata is not null)
        {
            descriptor = descriptor with
            {
                Metadata = metadata
            };
        }

        return Add(new SecretRecord
        {
            Descriptor = descriptor,
            Value = value
        });
    }

    public InMemorySecretResolverBuilder Add(
        SecretName name,
        string value,
        SecretVersion? version = null,
        SecretKind? kind = null,
        SecretMetadataText? displayName = null,
        SecretMetadataText? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Add(
            name,
            new SecretValue(value),
            version,
            kind,
            displayName,
            summary,
            metadata);
    }

    public InMemorySecretResolverBuilder Add(
        SecretName name,
        SecretValue value,
        SecretVersion? version = null,
        SecretKind? kind = null,
        SecretMetadataText? displayName = null,
        SecretMetadataText? summary = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name.Value))
            throw new ArgumentException("Secret name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(value);

        var descriptor = new SecretDescriptor
        {
            Name = name,
            Version = version?.Value,
            Kind = kind?.Value,
            DisplayName = displayName?.Value,
            Summary = summary?.Value
        };

        if (metadata is not null)
        {
            descriptor = descriptor with
            {
                Metadata = metadata
            };
        }

        return Add(new SecretRecord
        {
            Descriptor = descriptor,
            Value = value
        });
    }

    public IReadOnlyList<SecretRecord> BuildRecords()
        => records.ToArray();

    public InMemorySecretResolver BuildResolver()
        => new(BuildRecords());
}
