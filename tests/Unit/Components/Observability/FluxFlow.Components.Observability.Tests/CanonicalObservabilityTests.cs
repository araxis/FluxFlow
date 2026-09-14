using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class CanonicalObservabilityTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly IReadOnlyDictionary<string, Func<Action, FlowNode>> Factories =
        new Dictionary<string, Func<Action, FlowNode>>
        {
            ["counter"] = observe => new FlowCounterNode<ObservationInput>(
                new FlowCounterOptions { Predicate = "accepted" },
                new RecordingExpressionEngine((_, _, _) => { observe(); return true; })),
            ["logger"] = observe => new FlowLoggerNode<ObservationInput>(
                new FlowLoggerOptions { MessageTemplate = "{amount}", AttributeSelectors = ["amount"] },
                new Dictionary<string, IObservabilityValueSelector<ObservationInput>>
                {
                    ["amount"] = new InputSelector(input => { observe(); return input.Value; })
                }),
            ["metrics"] = observe => new FlowMetricsNode<ObservationInput>(
                new FlowMetricsOptions(),
                new InputSelector(input => { observe(); return input.Value; }))
        };

    [Theory]
    [InlineData("counter", "count")]
    [InlineData("logger", "sequence")]
    [InlineData("metrics", "count")]
    public async Task Component_runs_directly_with_canonical_ports_and_request_reply(string kind, string ordinal)
    {
        var calls = 0;
        var node = Factories[kind](() => calls++);
        await using var application = new FlowApplication();
        var component = application.AddWorkflow("main").Add(kind, node);
        component.Instance.ShouldBeSameAs(node);
        component.Input.Address.ShouldBe($"main.{kind}.Input");
        component.Output.Address.ShouldBe($"main.{kind}.Output");
        node.Ports.Inputs["Input"].ShouldBeSameAs(node.Input);
        node.Ports.Outputs["Output"].ShouldBeSameAs(node.Output);
        node.Ports.Events.ShouldBeSameAs(node.Events);
        await application.StartAsync();
        var request = Message(new ObservationInput(4));

        var response = await application.RequestAsync(component.Input, component.Output, request, Timeout);

        response.IsError.ShouldBeFalse();
        response.Value.Json.GetProperty(ordinal).GetInt64().ShouldBe(1);
        response.TraceId.ShouldBe(request.TraceId);
        response.CausationId.ShouldBe(request.MessageId);
        calls.ShouldBe(1);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("counter", "count", ObservabilityErrorCodeNames.CounterPredicateFailed)]
    [InlineData("logger", "sequence", ObservabilityErrorCodeNames.LoggerFailed)]
    [InlineData("metrics", "count", ObservabilityErrorCodeNames.MetricsFailed)]
    public async Task Invalid_materialization_is_in_band_and_does_not_advance_state(
        string kind, string ordinal, string errorCode)
    {
        var calls = 0;
        await using var node = Factories[kind](() => calls++);
        var output = Sink(node.Output);
        var invalid = Message("not an object");
        var valid = Message(new ObservationInput(4));
        (await node.Input.SendAsync(invalid)).ShouldBeTrue();
        (await node.Input.SendAsync(valid)).ShouldBeTrue();

        var failure = await output.ReceiveAsync().WaitAsync(Timeout);
        var success = await output.ReceiveAsync().WaitAsync(Timeout);

        failure.Error.ShouldNotBeNull().Code.ShouldBe(errorCode);
        failure.CorrelationId.ShouldBe(invalid.CorrelationId);
        success.IsError.ShouldBeFalse();
        success.CorrelationId.ShouldBe(valid.CorrelationId);
        success.Value.Json.GetProperty(ordinal).GetInt64().ShouldBe(1);
        calls.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("counter")]
    [InlineData("logger")]
    [InlineData("metrics")]
    public async Task Incoming_error_keeps_its_envelope_and_bypasses_selectors(string kind)
    {
        var calls = 0;
        await using var node = Factories[kind](() => calls++);
        var output = Sink(node.Output);
        var error = Message(4).WithError(new FlowError("upstream.failed", "Upstream failed.", "test"));

        (await node.Input.SendAsync(error)).ShouldBeTrue();

        (await output.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(error);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task Bounded_input_backpressures_until_the_active_selector_returns()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new FlowMetricsNode<ObservationInput>(
            new FlowMetricsOptions { BoundedCapacity = 1 }, new InputSelector(input =>
            {
                if (input.Value == 1)
                {
                    entered.TrySetResult();
                    if (!release.Wait(Timeout)) throw new TimeoutException("Selector was not released.");
                }
                return input.Value;
            }));
        var output = Sink(node.Output);
        Task<bool>? second = null;
        try
        {
            (await node.Input.SendAsync(Message(new ObservationInput(1)))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            second = node.Input.SendAsync(Message(new ObservationInput(2)));
            second.IsCompleted.ShouldBeFalse();
        }
        finally { release.Set(); }

        (await second!.WaitAsync(Timeout)).ShouldBeTrue();
        node.Complete();
        var firstResult = await output.ReceiveAsync().WaitAsync(Timeout);
        var secondResult = await output.ReceiveAsync().WaitAsync(Timeout);
        firstResult.Value.ShouldNotBeNull().Json.Deserialize<FlowMetricSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull().Count.ShouldBe(1);
        var finalSnapshot = secondResult.Value.ShouldNotBeNull().Json.Deserialize<FlowMetricSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
        finalSnapshot.Count.ShouldBe(2);
        finalSnapshot.TotalSize.ShouldBe(3);
        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Fault_waits_for_active_selector_before_disposal_completes()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new FlowMetricsNode<ObservationInput>(new FlowMetricsOptions(), new InputSelector(input =>
        {
            entered.TrySetResult();
            if (!release.Wait(Timeout)) throw new TimeoutException("Selector was not released.");
            return input.Value;
        }));
        var failure = new InvalidOperationException("Stop active observation.");
        Task? disposal = null;
        try
        {
            (await node.Input.SendAsync(Message(new ObservationInput(1)))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            node.Fault(failure);
            disposal = node.DisposeAsync().AsTask();
            node.Completion.IsCompleted.ShouldBeFalse();
            disposal.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.Set();
            await (disposal ?? node.DisposeAsync().AsTask()).WaitAsync(Timeout);
        }

        var observed = await Should.ThrowAsync<InvalidOperationException>(async () => await node.Completion);
        observed.ShouldBeSameAs(failure);
    }

    private static FlowMessage Message<T>(T value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private sealed record ObservationInput(int Value);

    private sealed class InputSelector(Func<ObservationInput, object?> select)
        : IObservabilityValueSelector<ObservationInput>
    {
        public object? Select(ObservationInput input, ObservabilityNodeContext context) => select(input);
    }
}
