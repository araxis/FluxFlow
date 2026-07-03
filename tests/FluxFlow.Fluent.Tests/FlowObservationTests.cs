using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Tests;

public sealed class FlowObservationTests
{
    [Fact]
    public async Task OnError_observes_a_node_error()
    {
        var observed = new TaskCompletionSource<FlowError>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var flow = Flow
            .From(new StringSourceNode(["x"]))
            .Then(new FaultingNode("boom"))
            .To(new CollectSinkNode(new StringCollector()))
            .OnError(error => observed.TrySetResult(error))
            .Build();

        await flow.StartAsync();

        var error = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Message.ShouldBe("boom");
        error.Exception.ShouldBeOfType<InvalidOperationException>();
    }

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
    public async Task Post_build_OnError_subscription_observes_and_returns_a_disposable()
    {
        var observed = new TaskCompletionSource<FlowError>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var flow = Flow
            .From(new StringSourceNode(["x"]))
            .Then(new FaultingNode("kaboom"))
            .To(new CollectSinkNode(new StringCollector()))
            .Build();

        using var subscription = flow.OnError(error => observed.TrySetResult(error));
        subscription.ShouldNotBeNull();

        await flow.StartAsync();

        var error = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        error.Message.ShouldBe("kaboom");
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
    public void OnError_rejects_a_null_handler()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.OnError(null!));
    }

    [Fact]
    public void OnEvent_rejects_a_null_handler()
    {
        var builder = Flow.From(new StringSourceNode(["x"]));
        Should.Throw<ArgumentNullException>(() => builder.OnEvent(null!));
    }
}
