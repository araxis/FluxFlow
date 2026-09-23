using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class MessageLineageTests
{
    [Fact]
    public void Each_generated_output_has_its_own_identity_and_the_same_input_cause()
    {
        var input = FlowMessage.Create(new ScalarValue<int>(1), new CorrelationId("request"),
            headers: new Dictionary<string, FlowValue> { ["customer"] = "42" });
        var left = input.WithValue(new ScalarValue<int>(2));
        var right = input.WithValue(new ScalarValue<int>(3));
        left.MessageId.ShouldNotBe(right.MessageId);
        foreach (FlowMessage output in new FlowMessage[] { left, right })
        {
            output.MessageId.ShouldNotBe(input.MessageId);
            output.CausationId.ShouldBe(input.MessageId);
            output.TraceId.ShouldBe(input.TraceId);
            output.CorrelationId.ShouldBe(input.CorrelationId);
            output.Headers.ShouldBeSameAs(input.Headers);
            output.Timestamp.ShouldBeGreaterThanOrEqualTo(input.Timestamp);
        }
    }

    [Fact]
    public void Error_and_recovery_use_the_same_generation_operation()
    {
        var input = FlowMessage.Create(JsonFlowValue.Null);
        var error = new FlowError("failed", "Expected failure", "test");
        var failure = input.WithValue(error);
        failure.IsError.ShouldBeTrue();
        failure.Value.ShouldBeSameAs(error);
        failure.Error.ShouldBeSameAs(error);
        failure.CausationId.ShouldBe(input.MessageId);
        var recovered = failure.WithValue(new ScalarValue<int>(42));
        recovered.IsError.ShouldBeFalse();
        recovered.Value.Data.ShouldBe(42);
        recovered.CausationId.ShouldBe(failure.MessageId);
        recovered.TraceId.ShouldBe(input.TraceId);
        FlowMessage.Create((FlowValue)error).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Generated_output_keeps_the_actual_payload_without_requesting_json()
    {
        var input = FlowMessage.Create(JsonFlowValue.Null);
        var payload = new InMemoryValue();
        var output = input.WithValue(payload);
        output.Value.ShouldBeSameAs(payload);
        ((FlowMessage)output).Value.ShouldBeSameAs(payload);
    }

    private sealed class InMemoryValue : FlowValue
    {
        protected override System.Text.Json.JsonElement CreateJson()
            => throw new InvalidOperationException("Generation must not materialize JSON.");
    }
}
