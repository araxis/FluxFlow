using FluxFlow.Components.Configuration.Contracts;
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Configuration;

public sealed class ConfigurationValidationRequestBuilder
{
    private readonly List<ConfigurationResourceReference> resources = [];
    private readonly List<SecretOptionReference> secrets = [];

    public ConfigurationValidationRequestBuilder AddResource(ConfigurationResourceReference resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resources.Add(resource);
        return this;
    }

    public ConfigurationValidationRequestBuilder AddResources(IEnumerable<ConfigurationResourceReference> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var resource in resources)
            AddResource(resource);

        return this;
    }

    public ConfigurationValidationRequestBuilder AddResource(
        string path,
        string resourceName,
        string? kind = null,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(resourceName);

        return AddResource(
            path,
            new ResourceReference
            {
                Name = new ResourceName(resourceName),
                Kind = kind
            },
            required,
            metadata);
    }

    public ConfigurationValidationRequestBuilder AddResource(
        string path,
        ResourceReference? reference,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var resource = new ConfigurationResourceReference
        {
            Path = path,
            Reference = reference,
            Required = required
        };

        if (metadata is not null)
        {
            resource = resource with
            {
                Metadata = metadata
            };
        }

        return AddResource(resource);
    }

    public ConfigurationValidationRequestBuilder AddResource(
        ConfigurationOptionPath path,
        ResourceName resourceName,
        ResourceKind? kind = null,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddResource(
            path,
            new ResourceReference
            {
                Name = resourceName,
                Kind = kind?.Value
            },
            required,
            metadata);

    public ConfigurationValidationRequestBuilder AddResource(
        ConfigurationOptionPath path,
        ResourceReference? reference,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(path.Value))
            throw new ArgumentException("Configuration option path cannot be empty.", nameof(path));

        var resource = new ConfigurationResourceReference
        {
            Path = path.Value,
            Reference = reference,
            Required = required
        };

        if (metadata is not null)
        {
            resource = resource with
            {
                Metadata = metadata
            };
        }

        return AddResource(resource);
    }

    public ConfigurationValidationRequestBuilder AddOptionalResource(
        string path,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddResource(path, reference: null, required: false, metadata);

    public ConfigurationValidationRequestBuilder AddOptionalResource(
        ConfigurationOptionPath path,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddResource(path, reference: null, required: false, metadata);

    public ConfigurationValidationRequestBuilder AddSecret(SecretOptionReference secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        secrets.Add(secret);
        return this;
    }

    public ConfigurationValidationRequestBuilder AddSecrets(IEnumerable<SecretOptionReference> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        foreach (var secret in secrets)
            AddSecret(secret);

        return this;
    }

    public ConfigurationValidationRequestBuilder AddSecret(
        string optionPath,
        string secretName,
        string? version = null,
        string? kind = null,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(optionPath);
        ArgumentNullException.ThrowIfNull(secretName);

        return AddSecret(
            optionPath,
            new SecretReference
            {
                Name = new SecretName(secretName),
                Version = version,
                Kind = kind
            },
            required,
            metadata);
    }

    public ConfigurationValidationRequestBuilder AddSecret(
        string optionPath,
        SecretReference? reference,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(optionPath);

        var secret = new SecretOptionReference
        {
            OptionPath = optionPath,
            Reference = reference,
            Required = required
        };

        if (metadata is not null)
        {
            secret = secret with
            {
                Metadata = metadata
            };
        }

        return AddSecret(secret);
    }

    public ConfigurationValidationRequestBuilder AddSecret(
        ConfigurationOptionPath optionPath,
        SecretName secretName,
        SecretVersion? version = null,
        SecretKind? kind = null,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddSecret(
            optionPath,
            new SecretReference
            {
                Name = secretName,
                Version = version?.Value,
                Kind = kind?.Value
            },
            required,
            metadata);

    public ConfigurationValidationRequestBuilder AddSecret(
        ConfigurationOptionPath optionPath,
        SecretReference? reference,
        bool required = true,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(optionPath.Value))
            throw new ArgumentException("Configuration option path cannot be empty.", nameof(optionPath));

        var secret = new SecretOptionReference
        {
            OptionPath = optionPath.Value,
            Reference = reference,
            Required = required
        };

        if (metadata is not null)
        {
            secret = secret with
            {
                Metadata = metadata
            };
        }

        return AddSecret(secret);
    }

    public ConfigurationValidationRequestBuilder AddOptionalSecret(
        string optionPath,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddSecret(optionPath, reference: null, required: false, metadata);

    public ConfigurationValidationRequestBuilder AddOptionalSecret(
        ConfigurationOptionPath optionPath,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AddSecret(optionPath, reference: null, required: false, metadata);

    public ConfigurationValidationRequest Build()
        => new()
        {
            Resources = resources.ToArray(),
            Secrets = secrets.ToArray()
        };
}
