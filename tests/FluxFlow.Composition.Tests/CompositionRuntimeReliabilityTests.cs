using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationRuntimeReliabilityTests
{
    [Fact]
    public async Task Component_output_port_preserves_real_flow_output_order_without_propagating_completion()
    {
        await using var output = new FlowOutput<FlowMessage<int>>(
            new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<FlowMessage<int>>();
        var outputPort = ComponentPorts.Output<int>("Output", output);
        var inputPort = ComponentPorts.Input<int>("Input", target);

        outputPort.Source.ShouldBeSameAs(output);
        inputPort.Target.ShouldBeSameAs(target);
        using var link = outputPort.Source.LinkTo(
            inputPort.Target,
            new DataflowLinkOptions { PropagateCompletion = false });

        foreach (var value in new[] { 1, 2, 3 })
        {
            (await output.SendAsync(FlowMessage.Create(value)))
                .ShouldBeTrue();
        }

        output.Complete();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var received = new[]
        {
            await target.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await target.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await target.ReceiveAsync(TimeSpan.FromSeconds(5))
        };
        received.Select(static message => message.Value).ShouldBe([1, 2, 3]);
        target.Completion.IsCompleted.ShouldBeFalse();

        target.Complete();
        await target.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsync_attempts_all_owned_cleanup_and_aggregates_failures()
    {
        var firstNode = new TrackingNode(throwOnDispose: true);
        var secondNode = new TrackingNode(throwOnDispose: false);
        var firstGraphLink = new TrackingDisposable(throwOnDispose: true);
        var secondGraphLink = new TrackingDisposable(throwOnDispose: false);
        var diagnosticLink = new TrackingDisposable(throwOnDispose: true);
        var events = new TrackingSourceBlock<FlowEvent>(diagnosticLink);
        var runtime = ApplicationRuntime.Create(
            [
                ComponentInstance.Create(firstNode, events: events),
                ComponentInstance.Create(secondNode)
            ],
            [firstGraphLink, secondGraphLink],
            []);

        var exception = await Should.ThrowAsync<AggregateException>(
            async () => await runtime.DisposeAsync());

        exception.InnerExceptions.Count.ShouldBe(3);
        firstNode.DisposeCount.ShouldBe(1);
        secondNode.DisposeCount.ShouldBe(1);
        firstGraphLink.DisposeCount.ShouldBe(1);
        secondGraphLink.DisposeCount.ShouldBe(1);
        diagnosticLink.DisposeCount.ShouldBe(1);
    }

    private sealed class TrackingNode(bool throwOnDispose) : IFlowNode
    {
        public int DisposeCount { get; private set; }

        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return throwOnDispose
                ? ValueTask.FromException(new InvalidOperationException("node cleanup failed"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingDisposable(bool throwOnDispose) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (throwOnDispose)
                throw new InvalidOperationException("link cleanup failed");
        }
    }

    private sealed class TrackingSourceBlock<T>(IDisposable link) : ISourceBlock<T>
    {
        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions) => link;

        public T? ConsumeMessage(
            DataflowMessageHeader messageHeader,
            ITargetBlock<T> target,
            out bool messageConsumed)
        {
            messageConsumed = false;
            return default;
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => false;

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
        }
    }
}
