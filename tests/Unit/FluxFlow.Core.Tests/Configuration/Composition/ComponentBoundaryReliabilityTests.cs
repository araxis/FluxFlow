using FluxFlow.Core.Application;
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
    public void Composition_package_has_no_secondary_runtime()
    {
        var assembly = typeof(FlowApplication).Assembly;
        assembly.GetType("FluxFlow.Composition.ApplicationRuntime").ShouldBeNull();
        assembly.GetType("FluxFlow.Composition.ApplicationRuntimeComponent").ShouldBeNull();
        assembly.GetType("FluxFlow.Composition.RuntimeNodeKey").ShouldBeNull();
    }

    [Fact]
    public async Task Component_output_port_preserves_real_flow_output_order_without_propagating_completion()
    {
        await using var output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = 1 });
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
        services.AddComponentFactory("main.node", _ => node).AddEventPort<TrackingNode>("main.node", _ => events);
        await using var provider = services.BuildServiceProvider();
        var instance = await provider.BuildApplicationAsync();

        var failure = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask().WaitAsync(Deadline));

        failure.Flatten().InnerExceptions.ShouldBe([linkFailure]);
        node.DisposeCalls.ShouldBe(0);
        events.DisposeCalls.ShouldBe(1);
        events.Target.ShouldNotBeNull().Completion.IsCompletedSuccessfully.ShouldBeTrue();

        (await Should.ThrowAsync<ApplicationException>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(linkFailure);
        node.DisposeCalls.ShouldBe(0);
        events.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_event_attachment_releases_target_without_disposing_supplied_node()
    {
        var failure = new InvalidOperationException("attachment failed");
        var node = new TrackingNode();
        var events = new TrackingSource(attachmentFailure: failure);
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => node).AddEventPort<TrackingNode>("main.node", _ => events);
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).ShouldBeSameAs(failure);

        var target = events.Target.ShouldNotBeNull();
        await target.Completion.WaitAsync(Deadline);
        target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        node.DisposeCalls.ShouldBe(0);
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
