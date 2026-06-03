using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationValidationRequest
{
    public IReadOnlyList<ConfigurationResourceReference> Resources { get; init; } = [];
    public IReadOnlyList<SecretOptionReference> Secrets { get; init; } = [];
}
