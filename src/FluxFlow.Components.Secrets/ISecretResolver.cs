using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public interface ISecretResolver
{
    ValueTask<SecretResolveResult> ResolveAsync(
        SecretReference reference,
        CancellationToken cancellationToken = default);
}
