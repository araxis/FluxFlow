using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentBoundaryReliabilityTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public void Composition_package_has_no_secondary_runtime()
    {
        var assembly = typeof(ComponentInstance).Assembly;
        assembly.GetType("FluxFlow.Composition.ApplicationRuntime").ShouldBeNull();
        assembly.GetType("FluxFlow.Composition.ApplicationRuntimeComponent").ShouldBeNull();
        assembly.GetType("FluxFlow.Composition.RuntimeNodeKey").ShouldBeNull();
    }

    [Fact]
    public async Task Component_output_port_preserves_real_flow_output_order_without_propagating_completion()
    {
        await using var output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<FlowMessage>();
        var outputPort = ComponentPorts.Output("Output", output);
        var inputPort = ComponentPorts.Input("Input", target);
        outputPort.Source.ShouldBeSameAs(output);
        inputPort.Target.ShouldBeSameAs(target);
        using var link = outputPort.Source.LinkTo(inputPort.Target, new DataflowLinkOptions { PropagateCompletion = false });
        foreach (var value in new[] { 1, 2, 3 })
            (await output.SendAsync(FlowMessage.Create(FlowValue.From(value)))).ShouldBeTrue();
        output.Complete();
        await output.Completion.WaitAsync(Deadline);
        var received = new[]
        {
            await target.ReceiveAsync(Deadline), await target.ReceiveAsync(Deadline), await target.ReceiveAsync(Deadline)
        };
        received.Select(message => message.Value!.Deserialize<int>()).ShouldBe([1, 2, 3]);
        target.Completion.IsCompleted.ShouldBeFalse();
        target.Complete();
        await target.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Component_cleanup_joins_event_target_and_reports_every_failure()
    {
        var nodeFailure = new InvalidOperationException("node cleanup failed");
        var callbackFailure = new ArgumentException("component cleanup failed");
        var linkFailure = new ApplicationException("event unlink failed");
        var node = new TrackingNode(nodeFailure);
        var events = new TrackingSource(linkFailure: linkFailure);
        var callbackCalls = 0;
        var instance = new ComponentInstance(node, events: events,
            disposeAsync: () => { callbackCalls++; return ValueTask.FromException(callbackFailure); });

        var failure = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask().WaitAsync(Deadline));

        failure.Flatten().InnerExceptions.ShouldBe([nodeFailure, callbackFailure, linkFailure], ignoreOrder: true);
        node.DisposeCalls.ShouldBe(1);
        callbackCalls.ShouldBe(1);
        events.DisposeCalls.ShouldBe(1);
        events.Target.ShouldNotBeNull().Completion.IsCompletedSuccessfully.ShouldBeTrue();
        instance.Activity.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        (await Record.ExceptionAsync(() => instance.Completion.WaitAsync(Deadline))).ShouldBeSameAs(linkFailure);
    }

    [Fact]
    public async Task Failed_event_attachment_releases_target_without_disposing_supplied_node()
    {
        var failure = new InvalidOperationException("attachment failed");
        var node = new TrackingNode();
        var events = new TrackingSource(attachmentFailure: failure);
        var callbacks = 0;

        Should.Throw<InvalidOperationException>(() => new ComponentInstance(node, events: events,
            disposeAsync: () => { callbacks++; return ValueTask.CompletedTask; })).ShouldBeSameAs(failure);

        var target = events.Target.ShouldNotBeNull();
        await target.Completion.WaitAsync(Deadline);
        target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        node.DisposeCalls.ShouldBe(0);
        callbacks.ShouldBe(0);
        events.DisposeCalls.ShouldBe(0);
    }

    private sealed class TrackingNode(Exception? disposeFailure = null) : IFlowNode
    {
        public Task Completion => Task.CompletedTask;
        public int DisposeCalls { get; private set; }
        public void Complete() { }
        public void Fault(Exception error) { }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(disposeFailure);
        }
    }

    private sealed class TrackingSource(Exception? attachmentFailure = null, Exception? linkFailure = null)
        : ISourceBlock<FlowMessage>, IDisposable
    {
        public ITargetBlock<FlowMessage>? Target { get; private set; }
        public Task Completion => Task.CompletedTask;
        public int DisposeCalls { get; private set; }
        public void Complete() { }
        public void Fault(Exception error) { }
        public IDisposable LinkTo(ITargetBlock<FlowMessage> target, DataflowLinkOptions options)
        {
            Target = target;
            if (attachmentFailure is not null) throw attachmentFailure;
            return this;
        }
        public void Dispose()
        {
            DisposeCalls++;
            if (linkFailure is not null) throw linkFailure;
        }
        public FlowMessage? ConsumeMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target, out bool consumed)
        { consumed = false; return null; }
        public bool ReserveMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target) => false;
        public void ReleaseReservation(DataflowMessageHeader header, ITargetBlock<FlowMessage> target) { }
    }
}
