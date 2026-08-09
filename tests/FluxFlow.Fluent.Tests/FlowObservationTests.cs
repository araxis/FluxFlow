using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowObservationTests
{
    [Fact]
    public async Task OnEvent_observes_a_node_event()
    {
        var observed = new TaskCompletionSource<FlowEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var flow = Flow
            .From(new StringSourceNode(["x"]))
            .OnEvent(@event =>
            {
                if (@event.Name == "tick")
                {
                    observed.TrySetResult(@event);
                }
            })
            .Then(new EventNode("tick"))
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        await flow.StartAsync();

        var @event = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe("tick");
    }

    [Fact]
    public async Task Flow_without_observers_still_completes_when_a_node_errors()
    {
        var collector = new StringCollector();

        await using var flow = Flow
            .From(new StringSourceNode(["x"]))
            .Then(new FaultingNode("ignored"))
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The faulting node swallowed the message as an error, so nothing reached the sink.
        collector.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Error_messages_remain_observable_workflow_data_on_the_canonical_graph()
    {
        var errors = new ErrorCollector();

        await using var flow = Flow
            .From(new StringSourceNode(["value"]))
            .Then(new FaultingNode("expected failure"))
            .To(new CollectErrorSinkNode(errors))
            .Build();

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

        await using var flow = Flow
            .From(new StringSourceNode(["value"]))
            .OnEvent(static _ => throw new InvalidOperationException("observer failure"))
            .Then(new EventNode("observed"))
            .To(new CollectSinkNode(collector))
            .Build();

        await flow.StartAsync();
        await flow.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["value"]);
    }

    [Fact]
    public void OnEvent_rejects_a_null_handler()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.OnEvent(null!));
    }
}
