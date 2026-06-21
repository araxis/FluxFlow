using Microsoft.Extensions.Hosting;

namespace FluxFlow.Composition.Hosting;

internal sealed class CompositionRuntimeHostedService(CompositionRuntimeHost host) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => host.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => host.StopAsync(cancellationToken);
}
