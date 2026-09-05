using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluxFlow.Engine.Hosting;

internal sealed class FluxFlowApplicationHostedService(
    FluxFlowApplication application,
    IOptions<FluxFlowApplicationOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.StartWithHost)
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (options.Value.StopWithHost)
            await application.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
