using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Coordination;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Http;


/// <summary>Publishes incoming requests and optionally waits for a correlated Response. Does not own a server.</summary>
public sealed class HttpTrigger : FlowNode, IFlowSource
{
    private readonly FlowRequestReply _requests;
    // Admission and stop must agree on which operations are allowed to finish.
    private readonly object _admissionGate = new();
    private int _activeRequests;
    private bool _stopRequested;
    private bool _started;
    public HttpTriggerShutdownMode ShutdownMode { get; }
    public ITargetBlock<FlowMessage> Response { get; }
    public int PendingCount => _requests.PendingCount;
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public HttpTrigger(int outputCapacity = 128, int maxPendingRequests = 1024, TimeProvider? timeProvider = null)
        : this(HttpTriggerShutdownMode.Cancel, outputCapacity, maxPendingRequests, timeProvider) { }

    public HttpTrigger(HttpTriggerShutdownMode shutdownMode, int outputCapacity = 128,
        int maxPendingRequests = 1024, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingRequests);
        if (!Enum.IsDefined(shutdownMode)) throw new ArgumentOutOfRangeException(nameof(shutdownMode));
        ShutdownMode = shutdownMode;
        _requests = new(maxPendingRequests, timeProvider);
        Output = new(static message => message, new() { BoundedCapacity = outputCapacity });
        Events = new(static message => message);
        Lifetime = new(this, produce: async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); }
            finally { _requests.Stop(); }
        });
        Response = new ActionBlock<FlowMessage>(message =>
        {
            if (_requests.Reply(nameof(Response), message))
                EmitEvent(message.WithValue(new FlowEvent { Name = "http.reply_received", Kind = FlowEventKind.Domain }));
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = outputCapacity });
        Lifetime.HasOutput(Output);
        Lifetime.HasTerminalInput(Response);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
        lock (_admissionGate) _started = true;
    }

    public void StopProduction()
    {
        lock (_admissionGate)
        {
            _stopRequested = true;
            if (ShutdownMode == HttpTriggerShutdownMode.Drain && _activeRequests != 0) return;
        }
        FinishProduction();
    }

    private void FinishProduction()
    {
        Response.Complete();
        Lifetime.StopProduction();
    }

    /// <summary>Acknowledges publication, not durable delivery or downstream completion.</summary>
    public async ValueTask PublishAsync(FlowMessage request, CancellationToken cancellationToken = default)
    {
        BeginRequest();
        try
        {
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(Stopping, cancellationToken);
            await PublishCoreAsync(request, stopped.Token).ConfigureAwait(false);
        }
        finally { EndRequest(); }
    }

    /// <summary>Publishes with a fresh correlation; first matching reply wins. Timeout covers publication and reply.</summary>
    public async Task<FlowMessage> RequestAsync(FlowMessage request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        BeginRequest();
        try
        {
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(Stopping, cancellationToken);
            return await _requests.RequestAsync(nameof(Response), request, PublishCoreAsync, timeout, stopped.Token).ConfigureAwait(false);
        }
        finally { EndRequest(); }
    }

    private void BeginRequest()
    {
        lock (_admissionGate)
        {
            Stopping.ThrowIfCancellationRequested();
            if (_stopRequested || !_started)
                throw new InvalidOperationException("The HTTP trigger is not accepting requests.");
            _activeRequests++;
        }
    }

    private void EndRequest()
    {
        lock (_admissionGate)
        {
            if (--_activeRequests != 0 || !_stopRequested || ShutdownMode != HttpTriggerShutdownMode.Drain) return;
        }
        FinishProduction();
    }

    private async ValueTask PublishCoreAsync(FlowMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Both callers already include Stopping; request/reply also cancels admission on timeout.
        cancellationToken.ThrowIfCancellationRequested();
        await EmitAsync(request, cancellationToken).ConfigureAwait(false);
        EmitEvent(request.WithValue(new FlowEvent { Name = "http.request_received", Kind = FlowEventKind.Domain }));
    }

    protected override ValueTask OnDisposeAsync() => _requests.DisposeAsync();
}
