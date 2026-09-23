using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

public sealed class MapperTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Typed_payload_and_lineage_are_preserved_without_serialization()
    {
        var input = new NumberValue(3);
        var output = new NumberValue(6);
        await using var mapper = new Mapper(new() { MapAsync = (value, _) =>
        {
            value.Value.ShouldBeSameAs(input);
            value.Headers["business"].ShouldBeSameAs(input);
            value.CorrelationId.ShouldNotBeNull();
            return ValueTask.FromResult<FlowValue>(output);
        }});
        var sink = Sink(mapper);
        var message = FlowMessage.Create(input, correlationId: CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["business"] = input });
        await mapper.Input.SendAsync(message);
        var result = await sink.ReceiveAsync(Deadline);
        result.Value.ShouldBeSameAs(output);
        result.MessageId.ShouldNotBe(message.MessageId);
        result.CausationId.ShouldBe(message.MessageId);
        result.TraceId.ShouldBe(message.TraceId);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.Headers["business"].ShouldBeSameAs(input);
        mapper.Complete();
        await mapper.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_is_error_data_and_following_array_remains_one_message(bool nullResult)
    {
        var calls = 0;
        var array = new JsonFlowValue(JsonSerializer.SerializeToElement(new[] { 1, 2 }));
        await using var mapper = new Mapper(new() { MapAsync = async (_, _) =>
        {
            await Task.Yield();
            if (++calls == 1)
            {
                if (nullResult) return null!;
                throw new InvalidOperationException("mapping failed");
            }
            return array;
        }});
        var sink = Sink(mapper);
        var existing = FlowMessage.Create(new FlowError("existing", "failed", "test"));
        await mapper.Input.SendAsync(existing);
        await mapper.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await mapper.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        mapper.Complete();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(existing);
        (await sink.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("node.processing_failed");
        (await sink.ReceiveAsync(Deadline)).Value.ShouldBeSameAs(array);
        await mapper.Completion.WaitAsync(Deadline);
        await sink.Completion.WaitAsync(Deadline);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Fault_cancels_inflight_function_and_joins_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = false;
        await using var mapper = new Mapper(new() { MapAsync = async (value, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return value.Value; }
            finally { exited = true; }
        }});
        await mapper.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await entered.Task.WaitAsync(Deadline);
        var error = new InvalidOperationException("stop");
        mapper.Fault(error);
        (await Record.ExceptionAsync(() => mapper.Completion.WaitAsync(Deadline))).ShouldBeSameAs(error);
        exited.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_options_fail_during_construction()
    {
        Should.Throw<ArgumentNullException>(() => new Mapper(null!));
        Should.Throw<ArgumentNullException>(() => new Mapper(new() { MapAsync = null! }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Mapper(new()
        { MapAsync = (value, _) => ValueTask.FromResult(value.Value), OutputCapacity = 0 }));
    }

    private static BufferBlock<FlowMessage> Sink(Mapper mapper)
    {
        var sink = new BufferBlock<FlowMessage>();
        mapper.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    private sealed class NumberValue(int number) : FlowValue
    {
        public int Number { get; } = number;
        protected override JsonElement CreateJson() => throw new InvalidOperationException("Unexpected JSON conversion");
    }
}
