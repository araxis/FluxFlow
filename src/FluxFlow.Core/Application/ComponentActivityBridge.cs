using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Application;

internal sealed class ComponentActivityBridge : IAsyncDisposable
{
    private const int Capacity = 256;

    private readonly string _componentAddress;
    private readonly string _workflowName;
    private readonly string _componentName;
    private readonly BroadcastBlock<FlowMessage> _activity = new(static message => message,
        new DataflowBlockOptions { BoundedCapacity = Capacity });
    private readonly FlowOutput<FlowMessage> _output = new(
        new FlowOutputOptions { Capacity = Capacity });
    private readonly ActionBlock<FlowMessage> _forwarder;
    private readonly IDisposable? _sourceLink;
    private readonly Task _completion;
    private int _disposed;

    public ComponentActivityBridge(
        string workflowName,
        string componentName,
        ISourceBlock<FlowMessage>? source,
        Task componentCompletion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        ArgumentNullException.ThrowIfNull(componentCompletion);

        _componentAddress = $"{workflowName}.{componentName}";
        _workflowName = workflowName;
        _componentName = componentName;
        _forwarder = new ActionBlock<FlowMessage>(
            ForwardAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = Capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        try
        {
            _sourceLink = source?.LinkTo(_forwarder, new DataflowLinkOptions { PropagateCompletion = false });
        }
        catch
        {
            _forwarder.Complete();
            _output.Complete();
            _activity.Complete();
            throw;
        }
        _completion = CompleteAsync(componentCompletion);
    }

    public ISourceBlock<FlowMessage> Output => _output;
    internal ISourceBlock<FlowMessage> Activity => _activity;
    internal Task Completion => _completion;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await _completion.ConfigureAwait(false);
        }
        finally
        {
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ForwardAsync(FlowMessage source)
    {
        var headers = source.Headers.ToDictionary(
            static item => item.Key,
            static item => item.Value,
            StringComparer.Ordinal);
        headers[FlowEventHeaders.Source] = _componentAddress;
        headers[FlowEventHeaders.Workflow] = _workflowName;
        headers[FlowEventHeaders.Component] = _componentName;
        var message = new FlowMessage(
                source.Value,
                source.MessageId,
                source.TraceId,
                source.Timestamp,
                source.CorrelationId,
                source.CausationId,
                headers);

        _activity.Post(message);
        if (await _output.SendAsync(message).ConfigureAwait(false))
            return;

        await _output.Completion.ConfigureAwait(false);
        throw new InvalidOperationException(
            "Component event output is no longer accepting data.");
    }

    private async Task CompleteAsync(Task componentCompletion)
    {
        Exception? unlinkFailure = null;
        try
        {
            await componentCompletion.ConfigureAwait(false);
        }
        catch
        {
            // Component completion remains the observable fault channel.
        }
        finally
        {
            try { _sourceLink?.Dispose(); }
            catch (Exception error) { unlinkFailure = error; }
            _forwarder.Complete();
        }

        try
        {
            await _forwarder.Completion.ConfigureAwait(false);
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _output.Fault(Unwrap(exception));
            await _output.Completion.ConfigureAwait(false);
        }
        finally
        {
            _activity.Complete();
            await _activity.Completion.ConfigureAwait(false);
        }
        if (unlinkFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unlinkFailure).Throw();
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerException!
            : exception;
}
