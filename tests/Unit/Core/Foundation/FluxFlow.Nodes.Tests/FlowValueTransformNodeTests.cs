using System.Text.Json;
using FluxFlow.Data;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowValueTransformNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Result_preserves_payload_identity_and_envelope_relationships()
    {
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Name = "sample" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await using var node = new Probe((input, _) => ValueTask.FromResult(input));
        var sink = Sink(node);
        var message = FlowMessage.Create(value, correlationId: CorrelationId.New(),
            headers: new Dictionary<string, string> { ["custom"] = "retained" });
        (await node.Input.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
        var result = await sink.ReceiveAsync(Timeout);
        result.Value.ShouldBeSameAs(value);
        result.MessageId.ShouldNotBe(message.MessageId);
        result.CausationId.ShouldBe(message.MessageId);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.TraceId.ShouldBe(message.TraceId);
        result.Headers["custom"].ShouldBe("retained");
        node.Complete();
        await node.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_result_or_local_conversion_does_not_stop_following_input(bool nullResult)
    {
        var calls = 0;
        await using var node = new Probe((input, _) =>
        {
            calls++;
            if (nullResult && input.Json.ValueKind == System.Text.Json.JsonValueKind.String)
                return ValueTask.FromResult<FlowValue>(null!);
            return ValueTask.FromResult((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(input.Json.Deserialize<int>() * 2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        });
        var sink = Sink(node);
        var existingError = FlowMessage.CreateError(new FlowError("invalid", "Invalid", "input"));
        foreach (var message in new[] { existingError, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("bad", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(21, new JsonSerializerOptions(JsonSerializerDefaults.Web)))) })
            (await node.Input.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
        (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(existingError);
        (await sink.ReceiveAsync(Timeout)).Error!.Code.ShouldBe("node.processing_failed");
        (await sink.ReceiveAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
        calls.ShouldBe(2);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Concurrent_processing_preserves_order_and_receives_stopping_token()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        await using var node = new Probe(async (input, stopping) =>
        {
            token = stopping;
            if (input.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1) await release.Task;
            else entered.TrySetResult();
            return input;
        }, new() { MaxDegreeOfParallelism = 2 });
        var sink = Sink(node);
        try
        {
            (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).WaitAsync(Timeout)).ShouldBeTrue();
            (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).WaitAsync(Timeout)).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            token.CanBeCanceled.ShouldBeTrue();
        }
        finally { release.TrySetResult(); }
        (await sink.ReceiveAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(1);
        (await sink.ReceiveAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        await node.DisposeAsync();
        token.IsCancellationRequested.ShouldBeTrue();
    }

    private sealed class Probe(Func<FlowValue, CancellationToken, ValueTask<FlowValue>> process,
        FlowNodeOptions? options = null) : FlowValueTransformNode(options)
    {
        protected override ValueTask<FlowValue> ProcessAsync(FlowValue input, CancellationToken cancellationToken)
            => process(input, cancellationToken);
    }

    private static BufferBlock<FlowMessage> Sink(FlowValueTransformNode node)
    {
        var sink = new BufferBlock<FlowMessage>();
        node.Output.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
