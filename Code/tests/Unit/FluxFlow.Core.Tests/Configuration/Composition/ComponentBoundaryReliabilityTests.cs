using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentBoundaryReliabilityTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Component_output_port_preserves_real_flow_output_order_without_propagating_completion()
    {
        var output = new BroadcastBlock<FlowMessage>(static value => value, new DataflowBlockOptions { BoundedCapacity = 1 });
        var target = new BufferBlock<FlowMessage>();
        using var link = output.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = false });
        foreach (var value in new[] { 1, 2, 3 })
            (await output.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        output.Complete();
        await output.Completion.WaitAsync(Deadline);
        var received = new[]
        {
            await target.ReceiveAsync(Deadline), await target.ReceiveAsync(Deadline), await target.ReceiveAsync(Deadline)
        };
        received.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1, 2, 3]);
        target.Completion.IsCompleted.ShouldBeFalse();
        target.Complete();
        await target.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Event_cleanup_joins_target_and_DI_stops_at_the_failing_bridge()
    {
        var nodeFailure = new InvalidOperationException("node cleanup failed");
        var callbackFailure = new ArgumentException("component cleanup failed");
        var linkFailure = new ApplicationException("event unlink failed");
        var node = new TrackingNode(new AggregateException(nodeFailure, callbackFailure));
        var events = new TrackingSource(linkFailure: linkFailure);
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node).AddEventPort<TrackingNode>(ComponentAddress.Parse("main.node"), _ => events);
        await using var provider = services.BuildServiceProvider();
        var instance = await provider.BuildApplicationAsync();

        var failure = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask().WaitAsync(Deadline));

        failure.Flatten().InnerExceptions.ShouldBe([linkFailure]);
        node.DisposeCalls.ShouldBe(0);
        events.DisposeCalls.ShouldBe(1);
        events.Target.ShouldNotBeNull().Completion.IsCompletedSuccessfully.ShouldBeTrue();

        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(linkFailure);
        node.DisposeCalls.ShouldBe(0);
        events.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Event_forwarding_and_unlink_failures_are_both_reported_without_duplicates()
    {
        var forwardingFailure = new InvalidOperationException("forwarding failed");
        var unlinkFailure = new ApplicationException("unlink failed");
        var componentFailure = new ArgumentException("component failed separately");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new TrackingSource(linkFailure: unlinkFailure);
        var bridge = new ComponentEventBridge(ComponentAddress.Create("main", "node"), events, done.Task);
        events.Target.ShouldNotBeNull().Fault(forwardingFailure);
        await Should.ThrowAsync<Exception>(() => bridge.Output.Completion.WaitAsync(Deadline));
        done.TrySetException(componentFailure);
        var error = await Should.ThrowAsync<AggregateException>(() => bridge.Completion.WaitAsync(Deadline));
        error.InnerExceptions.Count.ShouldBe(2);
        error.InnerExceptions.ShouldContain(forwardingFailure);
        error.InnerExceptions.ShouldContain(unlinkFailure);
        error.InnerExceptions.ShouldNotContain(componentFailure);
        events.DisposeCalls.ShouldBe(1);
        (await Should.ThrowAsync<AggregateException>(() => bridge.DisposeAsync().AsTask())).ShouldBeSameAs(error);
        events.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Event_bridge_dispose_callers_share_completion_and_cleanup(bool fail)
    {
        var failure = fail ? new ApplicationException("unlink failed") : null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new TrackingSource(linkFailure: failure);
        var bridge = new ComponentEventBridge(ComponentAddress.Create("main", "node"), events, done.Task);
        var first = bridge.DisposeAsync().AsTask();
        var second = bridge.DisposeAsync().AsTask();
        try
        {
            first.ShouldBeSameAs(bridge.Completion);
            second.ShouldBeSameAs(first);
            second.IsCompleted.ShouldBeFalse();
            events.DisposeCalls.ShouldBe(0);
        }
        finally { done.TrySetResult(); }
        foreach (var task in new[] { first, second, bridge.DisposeAsync().AsTask() })
        {
            if (failure is null) await task.WaitAsync(Deadline);
            else (await Should.ThrowAsync<ApplicationException>(() => task.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        }
        events.DisposeCalls.ShouldBe(1);
        events.Target!.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Event_output_failure_after_forwarder_complete_releases_queued_results()
    {
        var failure = new InvalidOperationException("event destination failed");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var offering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var events = new TrackingSource();
        var bridge = new ComponentEventBridge(ComponentAddress.Create("main", "node"), events, done.Task);
        using var link = bridge.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>(), new(), _ =>
        {
            offering.TrySetResult();
            release.Wait(Deadline);
            throw failure;
        });
        var target = events.Target.ShouldNotBeNull();
        try
        {
            target.Post(FlowMessage.Create(JsonFlowValue.Null)).ShouldBeTrue();
            await offering.Task.WaitAsync(Deadline);
            // Exceed the output's capacity while its consumer is blocked, leaving
            // transformed results to drain after the input has completed.
            for (var i = 0; i < 300; i++)
                (await target.SendAsync(FlowMessage.Create(JsonFlowValue.Null)).WaitAsync(Deadline)).ShouldBeTrue();
            target.Complete();
        }
        finally
        {
            done.TrySetResult();
            release.Set();
        }
        (await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => bridge.DisposeAsync().AsTask()))
            .ShouldBeSameAs(failure);
        target.Completion.IsCompleted.ShouldBeTrue();
        events.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_event_attachment_releases_target_without_disposing_supplied_node()
    {
        var failure = new InvalidOperationException("attachment failed");
        var node = new TrackingNode();
        var events = new TrackingSource(attachmentFailure: failure);
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node).AddEventPort<TrackingNode>(ComponentAddress.Parse("main.node"), _ => events);
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).ShouldBeSameAs(failure);

        var target = events.Target.ShouldNotBeNull();
        await target.Completion.WaitAsync(Deadline);
        target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        node.DisposeCalls.ShouldBe(1);
        events.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        node.DisposeCalls.ShouldBe(1);
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
