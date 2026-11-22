using DotNet.Testcontainers.Configurations;

namespace FluxFlow.Environments.Testcontainers;

public sealed class DockerRuntimeCheck : IContainerRuntimeCheck
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.Empty).Build();
        await client.System.PingAsync(cancellationToken).ConfigureAwait(false);
    }
}
