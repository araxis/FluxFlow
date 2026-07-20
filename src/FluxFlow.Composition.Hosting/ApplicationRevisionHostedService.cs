using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluxFlow.Composition.Hosting;

internal sealed class ApplicationRevisionHostedService(
    ApplicationRevisionHost host,
    IOptions<ApplicationRevisionHostingOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.StartApplicationWithHost)
            await host.StartApplicationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (options.Value.StopApplicationWithHost)
            await host.StopApplicationAsync(cancellationToken).ConfigureAwait(false);
    }
}
