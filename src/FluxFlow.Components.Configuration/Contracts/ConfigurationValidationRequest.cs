using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationValidationRequest
{
    private IReadOnlyList<ConfigurationResourceReference>? _resources = [];
    private IReadOnlyList<SecretOptionReference>? _secrets = [];

    public IReadOnlyList<ConfigurationResourceReference> Resources
    {
        get => _resources!;
        init => _resources = value?.ToArray();
    }

    public IReadOnlyList<SecretOptionReference> Secrets
    {
        get => _secrets!;
        init => _secrets = value?.ToArray();
    }
}
