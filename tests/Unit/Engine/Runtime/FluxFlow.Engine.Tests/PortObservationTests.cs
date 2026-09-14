using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class PortObservationTests
{
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort("Main", "Orders", "Output");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Typed_observation_exposes_one_native_canonical_projection_block()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output, discardUnmatched: true).Build();
        var source = new BufferBlock<FlowMessage>();
        using var attachment = runtime.AttachOutput(Output, source);
        var result = await runtime.ObserveAsync<Order>(Output, capacity: 1);
        await using var observation = result.Observation.ShouldNotBeNull();
        observation.Messages.ShouldBeAssignableTo<IPropagatorBlock<FlowMessage, FlowMessage<Order>>>();
        var message = Message((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Order(73), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        source.Post(message).ShouldBeTrue();
        var received = await observation.Messages.ReceiveAsync().WaitAsync(Deadline);

        received.ShouldBe(message.ConvertValue(new Order(73)));
        received.Headers.ShouldBeSameAs(message.Headers);
        observation.Capacity.ShouldBe(1);
        observation.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Projection_failure_is_isolated_without_a_false_overflow_report()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output, discardUnmatched: true).Build();
        var source = new BufferBlock<FlowMessage>();
        using var attachment = runtime.AttachOutput(Output, source);
        var invalidResult = await runtime.ObserveAsync<int>(Output, capacity: 2);
        var healthyResult = await runtime.ObserveAsync<FlowValue>(Output, capacity: 2);
        await using var invalid = invalidResult.Observation.ShouldNotBeNull();
        await using var healthy = healthyResult.Observation.ShouldNotBeNull();
        var malformed = Message((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("not-an-integer", new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        source.Post(malformed).ShouldBeTrue();
        var failure = await Should.ThrowAsync<JsonException>(async () =>
            await invalid.Completion.WaitAsync(Deadline));
        failure.Path.ShouldBe("$");
        (await healthy.Messages.ReceiveAsync().WaitAsync(Deadline)).Value.ShouldBeSameAs(malformed.Value);

        var replacementResult = await runtime.ObserveAsync<int>(Output, capacity: 1);
        await using var replacement = replacementResult.Observation.ShouldNotBeNull();
        var valid = Message((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(73, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        source.Post(valid).ShouldBeTrue();

        (await replacement.Messages.ReceiveAsync().WaitAsync(Deadline)).ShouldBe(valid.ConvertValue(73));
        (await healthy.Messages.ReceiveAsync().WaitAsync(Deadline)).Value.ShouldBeSameAs(valid.Value);
        ((IReceivableSourceBlock<ApplicationPortRejection>)runtime.Rejections).TryReceive(out _).ShouldBeFalse();
        runtime.Status.State.ShouldBe(ApplicationRuntimeState.Active);
        healthy.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Error_projection_preserves_error_and_identity_without_materializing_a_value()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output, discardUnmatched: true).Build();
        var source = new BufferBlock<FlowMessage>();
        using var attachment = runtime.AttachOutput(Output, source);
        var result = await runtime.ObserveAsync<Order>(Output, capacity: 1);
        await using var observation = result.Observation.ShouldNotBeNull();
        var error = new FlowError("order.invalid", "Order validation failed.", "validation");
        var message = FlowMessage.RestoreError(error, new MessageId("error-message"), new TraceId("error-trace"),
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"), new CorrelationId("error-exchange"),
            new MessageId("error-cause"), new Dictionary<string, string> { ["source"] = "orders" });

        source.Post(message).ShouldBeTrue();
        var received = await observation.Messages.ReceiveAsync().WaitAsync(Deadline);

        received.ShouldBe(message.ConvertError<Order>());
        received.Error.ShouldBeSameAs(error);
        received.Headers.ShouldBeSameAs(message.Headers);
    }

    private static FlowMessage Message(FlowValue value)
        => FlowMessage.Restore(value, new MessageId("observed-message"), new TraceId("observed-trace"),
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"), new CorrelationId("observed-exchange"),
            new MessageId("observed-cause"), new Dictionary<string, string> { ["source"] = "orders" });

    private sealed record Order(int OrderId);
}
