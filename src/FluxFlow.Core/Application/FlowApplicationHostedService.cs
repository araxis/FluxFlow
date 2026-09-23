using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Core.Application;

internal sealed class FlowApplicationHostedService(IServiceProvider services, FlowApplication application) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await application.ComposeAsync(services, application.LinkCapacity, cancellationToken).ConfigureAwait(false);
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await application.Completion.WaitAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await application.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally { await base.StopAsync(cancellationToken).ConfigureAwait(false); }
    }
}
