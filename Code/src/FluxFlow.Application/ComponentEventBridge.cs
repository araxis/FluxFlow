using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Application;

internal sealed class ComponentEventBridge : IAsyncDisposable
{
    private const int Capacity = 256;

    private readonly ComponentAddress _address;
    private readonly BroadcastBlock<FlowMessage> _output = new(static message => message,
        new DataflowBlockOptions { BoundedCapacity = Capacity });
    private readonly TransformBlock<FlowMessage, FlowMessage> _forwarder;

    public ComponentEventBridge(
        ComponentAddress address,
        ISourceBlock<FlowMessage> source,
        Task componentCompletion)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(componentCompletion);

        _address = address;
        _forwarder = new TransformBlock<FlowMessage, FlowMessage>(
            AddAddressHeaders,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = Capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _forwarder.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        IDisposable sourceLink;
        try
        {
            sourceLink = source.LinkTo(_forwarder, new DataflowLinkOptions());
        }
        catch
        {
            _forwarder.Complete();
            throw;
        }
        Completion = CompleteAsync(componentCompletion, sourceLink);
    }

    public ISourceBlock<FlowMessage> Output => _output;
    internal Task Completion { get; }

    public ValueTask DisposeAsync() => new(Completion);

    private FlowMessage AddAddressHeaders(FlowMessage source)
    {
        var headers = source.Headers.ToDictionary(
            static item => item.Key,
            static item => item.Value,
            StringComparer.Ordinal);
        headers[FlowEventHeaders.Source] = _address.Value;
        headers[FlowEventHeaders.Workflow] = _address.WorkflowName;
        headers[FlowEventHeaders.Component] = _address.ComponentName;
        return source.WithHeaders(headers);
    }

    private async Task ObserveOutputAsync()
    {
        try
        {
            await _output.Completion.ConfigureAwait(false);
            if (!_forwarder.Completion.IsCompleted)
                throw new InvalidOperationException(
                    "Component event output completed before event forwarding stopped.");
        }
        // Native completion propagation is downstream only. A failed output must
        // also release any results waiting in the transform block.
        catch (Exception error)
        {
            ((IDataflowBlock)_forwarder).Fault(error);
            // Fault can lose a race with Complete. Drain remaining results when
            // the output has already stopped accepting them.
            _forwarder.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
            throw;
        }
    }

    private async Task CompleteAsync(Task componentCompletion, IDisposable sourceLink)
    {
        var completion = Task.WhenAll(
            StopForwardingAsync(componentCompletion, sourceLink),
            _forwarder.Completion, ObserveOutputAsync());
        try { await completion.ConfigureAwait(false); }
        catch when (completion.Exception is not null)
        {
            // Forwarder and output can report the same propagated failure. Preserve
            // distinct failures, including unlink errors, after all work has settled.
            var errors = completion.Exception.Flatten().InnerExceptions
                .Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (errors.Length == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Component event forwarding and cleanup failed.", errors);
        }
    }

    private async Task StopForwardingAsync(Task componentCompletion, IDisposable sourceLink)
    {
        try
        {
            await componentCompletion.ConfigureAwait(false);
        }
        catch
        {
            // Component completion remains the observable fault channel.
        }
        try { sourceLink.Dispose(); }
        finally { _forwarder.Complete(); }
    }
}
