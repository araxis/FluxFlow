using FluxFlow.Core;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Observability.Tests.Direct;

[Collection("Process diagnostics")]
public sealed class FlowObservationTests
{
    [Fact]
    public async Task Disposing_observation_leaves_sources_running_and_closes_observation()
    {
        var source = new BroadcastBlock<FlowMessage>(static message => message);
        var events = new FlowEventStream(source);
        await events.DisposeAsync();
        await events.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        source.Completion.IsCompleted.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => events.OnEvent(static _ => { }));
        await events.DisposeAsync();
        source.Complete();
        await source.Completion;
    }

    [Fact]
    public async Task Observation_waits_for_all_sources_and_preserves_source_faults()
    {
        var first = new BroadcastBlock<FlowMessage>(static message => message);
        var last = new BroadcastBlock<FlowMessage>(static message => message);
        await using var events = new FlowEventStream(first, last);
        first.Complete();
        await first.Completion;
        events.Events.Completion.IsCompleted.ShouldBeFalse();
        ((IDataflowBlock)last).Fault(new InvalidOperationException("source failed"));
        await Should.ThrowAsync<InvalidOperationException>(() => events.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task OnEvent_observes_a_node_event()
    {
        var observed = new TaskCompletionSource<FlowEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        var node = new EventNode("tick");
        await using var flow = Build(node, new CollectSinkNode(new StringCollector()));
        await using var events = new FlowEventStream(node.Events);
        using var subscription = events.OnEvent(value => observed.TrySetResult(value));

        await flow.StartAsync();

        var @event = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe("tick");
    }

    [Fact]
    public async Task Flow_without_observers_still_completes_when_a_node_errors()
    {
        var collector = new StringCollector();

        await using var flow = Build(new FaultingNode("ignored"), new CollectSinkNode(collector));

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The faulting node swallowed the message as an error, so nothing reached the sink.
        collector.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Error_messages_remain_observable_workflow_data_on_the_canonical_graph()
    {
        var errors = new ErrorCollector();

        await using var flow = Build(new FaultingNode("expected failure"), new CollectErrorSinkNode(errors));

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = errors.Items.ShouldHaveSingleItem();
        error.Code.ShouldBe("node.processing_failed");
        error.Message.ShouldBe("expected failure");
        error.Category.ShouldBe("processing");
        error.IsTransient.ShouldBeFalse();
        error.Details.ShouldNotBeNull()
            .GetProperty("exceptionType").GetString()
            .ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task Throwing_event_observer_is_isolated_from_canonical_workflow_delivery()
    {
        var collector = new StringCollector();

        var node = new EventNode("observed");
        await using var flow = Build(node, new CollectSinkNode(collector));
        await using var events = new FlowEventStream(node.Events);
        using var subscription = events.OnEvent(static _ => throw new InvalidOperationException("observer failure"));

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["value"]);
    }

    [Fact]
    public async Task OnEvent_rejects_a_null_handler()
    {
        await using var builder = new FlowEventStream();
        Should.Throw<ArgumentNullException>(() => builder.OnEvent(null!));
    }
    private static FlowApplication Build(IFlowComponent node, IFlowComponent sink)
    {
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var input = workflow.Add("source", new StringSourceNode(["value"]));
        var process = workflow.Add("process", node);
        var target = workflow.Add("sink", sink);
        input.Output.LinkTo(process.Input);
        process.Output.LinkTo(target.Input);
        foreach (var output in target.Outputs.Values) output.DiscardUnmatched();
        return app;
    }
}
