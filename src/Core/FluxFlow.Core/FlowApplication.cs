using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>Owns a directly constructed graph. Definition adapters are optional callers.</summary>
public sealed class FlowApplication : IAsyncDisposable
{
    private readonly FlowTopology _topology = new();
    private readonly CancellationTokenSource _ingressStopped = new();
    private readonly int _linkCapacity;
    private readonly FlowRequestReply _exchanges;
    private readonly IAsyncDisposable? _lifetimeOwner;
    private FlowExecution? _execution;
    private Task? _start;
    private Task? _stop;
    private Task? _dispose;
    private bool _stopped;

    public FlowApplication(int linkCapacity = 128, int maxPendingRequests = 1024, TimeProvider? timeProvider = null)
    {
        if (linkCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(linkCapacity));
        _linkCapacity = linkCapacity;
        _exchanges = new(maxPendingRequests, timeProvider);
    }

    internal FlowApplication(IAsyncDisposable lifetimeOwner) : this()
        => _lifetimeOwner = lifetimeOwner ?? throw new ArgumentNullException(nameof(lifetimeOwner));

    public FlowWorkflow AddWorkflow(string name) => _topology.AddWorkflow(name, this);
    public void Validate() => _topology.Validate();
    public FlowInputEndpoint GetInput(string address) => _topology.GetInput(address);
    public FlowOutputEndpoint GetOutput(string address) => _topology.GetOutput(address);
    public int PendingRequestCount => _exchanges.PendingCount;

    /// <summary>Binds existing port addresses without starting the application. Callers control waiting through cancellation.</summary>
    public FlowFunction MakeFunction(string inputAddress, string replyAddress)
        => MakeFunction(GetInput(inputAddress), GetOutput(replyAddress));

    /// <summary>Creates a callable request delegate. The application retains ownership and must be running when invoked.</summary>
    public FlowFunction MakeFunction(FlowInputEndpoint input, FlowOutputEndpoint reply)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(reply);
        _topology.ValidateRequestPorts(input, reply);
        return (message, cancellationToken) => RequestAsync(input, reply, message, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>Starts a new exchange; the timeout includes input admission and reply waiting.</summary>
    public Task<FlowMessage> RequestAsync(string inputAddress, string replyAddress, FlowMessage message,
        TimeSpan timeout, CancellationToken cancellationToken = default)
        => RequestAsync(GetInput(inputAddress), GetOutput(replyAddress), message, timeout, cancellationToken);

    /// <summary>Starts a request without requiring a predeclared reply output.</summary>
    public Task<FlowMessage> RequestAsync(FlowInputEndpoint input, FlowOutputEndpoint reply, FlowMessage message,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(message);
        lock (_topology.Gate)
        {
            _topology.ValidateRequestPorts(input, reply);
            if (_stopped || _execution is null || _execution.Completion.IsCompleted)
                throw new InvalidOperationException("Application is not running.");
            return _exchanges.RequestAsync(reply.Address, message,
                (envelope, token) =>
                {
                    _execution.EnableRequestReplies(reply);
                    return AdmitRequestAsync(input, envelope, token);
                }, timeout, cancellationToken);
        }
    }

    private static async ValueTask AdmitRequestAsync(FlowInputEndpoint input, FlowMessage message,
        CancellationToken cancellationToken)
    {
        if (!await input.Block.SendAsync(message, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Input '{input.Address}' rejected the request.");
    }

    public Task Completion
    {
        get
        {
            lock (_topology.Gate)
                return _execution?.Completion ?? throw new InvalidOperationException("Application has not started.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_topology.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopped || _dispose is not null) throw new InvalidOperationException("Application has stopped.");
            if (_start is not null) return _start;
            _execution = new FlowExecution(_topology.Freeze(), _linkCapacity, _exchanges);
            return _start = StartCoreAsync(_execution, cancellationToken);
        }
    }

    private async Task StartCoreAsync(FlowExecution execution, CancellationToken cancellationToken)
    {
        try { await execution.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception startupError)
        {
            try { await DisposeAsync().ConfigureAwait(false); }
            catch (AggregateException shutdownErrors)
            {
                var cleanupErrors = shutdownErrors.Flatten().InnerExceptions
                    .Where(error => !ReferenceEquals(error, startupError)).ToArray();
                if (cleanupErrors.Length != 0)
                    throw new AggregateException("Application startup and cleanup failed.", [startupError, .. cleanupErrors]);
            }
            throw;
        }
    }

    /// <summary>Waits for local input admission, not processing or durable acknowledgement.</summary>
    public async ValueTask<bool> SendAsync(FlowInputEndpoint input, FlowMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(message);
        lock (_topology.Gate)
        {
            if (input.Topology != _topology) throw new ArgumentException("Input belongs to a different application.", nameof(input));
            if (_stopped || _execution is null || _execution.Completion.IsCompleted)
                throw new InvalidOperationException("Application is not running.");
        }
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _ingressStopped.Token);
        return await input.Block.SendAsync(message, admission.Token).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopping;
        lock (_topology.Gate)
        {
            stopping = _stop ??= StopCoreAsync();
        }
        try { await stopping.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            Fault(error);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        _stopped = true;
        List<Exception>? errors = null;
        try { _ingressStopped.Cancel(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        try { _exchanges.Stop(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        if (_execution is not null)
        {
            try { _execution.Stop(); }
            catch (Exception error) { (errors ??= []).Add(error); }
            try { await _execution.Completion.ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        else
        {
            var components = _topology.Close();
            var nodes = components.Select(component => component.Instance).ToArray();
            var lifetime = new FlowLifetime(nodes, nodes);
            try { lifetime.Stop(); }
            catch (Exception error) { (errors ??= []).Add(error); }
            try
            {
                await lifetime.WaitForCompletionAsync(components.Select(component => component.Node.Completion)
                    .Append(lifetime.CompleteControlsAfterAsync([]))).ConfigureAwait(false);
            }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        ThrowShutdownFailures(errors);
    }

    public void Fault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_topology.Gate)
        {
            _stopped = true;
            List<Exception>? errors = null;
            try { _ingressStopped.Cancel(); }
            catch (Exception cleanupError) { (errors ??= []).Add(cleanupError); }
            try { _exchanges.Stop(error); }
            catch (Exception cleanupError) { (errors ??= []).Add(cleanupError); }
            _execution?.Fault(error);
            ThrowShutdownFailures(errors);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_topology.Gate) return new ValueTask(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        var components = _topology.Close();
        List<Exception>? errors = null;
        try { await StopAsync().ConfigureAwait(false); }
        catch (Exception error) { (errors ??= []).Add(error); }
        foreach (var component in components.Reverse())
        {
            try
            {
                if (component.Ownership == ComponentOwnership.Owned)
                    await component.Instance.DisposeAsync().ConfigureAwait(false);
                else
                    await component.Instance.Completion.ConfigureAwait(false);
            }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        try
        {
            if (_execution is not null) await _execution.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) { (errors ??= []).Add(error); }
        try { await _exchanges.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { (errors ??= []).Add(error); }
        finally { _ingressStopped.Dispose(); }
        try
        {
            if (_lifetimeOwner is not null) await _lifetimeOwner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException("Application shutdown failed.", errors);
    }

    private static void ThrowShutdownFailures(List<Exception>? errors)
    {
        if (errors is null) return;
        var distinct = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        throw new AggregateException("Application shutdown failed.", distinct);
    }
}
