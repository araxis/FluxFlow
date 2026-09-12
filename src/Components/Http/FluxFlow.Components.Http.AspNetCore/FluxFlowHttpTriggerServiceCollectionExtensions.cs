using FluxFlow.Components.RequestReply;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Components.Http.AspNetCore;

public static class FluxFlowHttpTriggerServiceCollectionExtensions
{
    /// <summary>
    /// Registers an HTTP trigger under <paramref name="name"/>: a keyed
    /// <see cref="HttpTriggerNode"/> and a hosted service that starts
    /// (and disposes) the trigger with the app. Wire the graph in <paramref name="configure"/>
    /// (link <c>trigger.Output</c> to your handler and the handler back to
    /// <c>trigger.Responses</c>). Expose it with <c>MapFluxFlowTrigger(pattern, name)</c>.
    /// </summary>
    public static IServiceCollection AddFluxFlowHttpTrigger(
        this IServiceCollection services,
        string name,
        Action<HttpTriggerNode> configure,
        RequestReplyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var normalizedName = name.Trim();

        services.AddKeyedSingleton(normalizedName, (_, _) =>
        {
            var node = new HttpTriggerNode(options);
            configure(node);
            return node;
        });
        services.AddSingleton<IHostedService>(provider => new HttpTriggerLifetime(provider, normalizedName));
        return services;
    }

    // Instantiates the keyed trigger at startup (which wires the graph and starts
    // consuming the source) and disposes it on shutdown.
    private sealed class HttpTriggerLifetime(IServiceProvider services, string name)
        : IHostedService, IAsyncDisposable
    {
        private HttpTriggerNode? _node;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _node = services.GetRequiredKeyedService<HttpTriggerNode>(name);
            return _node.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_node is null) return;
            _node.Complete();
            try { await _node.Completion.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
            {
                _node.Fault(error);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_node is not null)
            {
                await _node.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
