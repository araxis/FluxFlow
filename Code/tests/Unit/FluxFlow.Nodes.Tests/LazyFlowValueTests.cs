using FluxFlow.Core.Nodes;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class LazyFlowValueTests
{
    [Fact]
    public async Task Event_attributes_and_details_preserve_typed_values_until_concurrent_json_reads()
    {
        var value = new TextValue("hello");
        var message = new FlowEvent
        {
            Name = "typed.event",
            Details = value,
            Attributes = new Dictionary<string, object?>
            {
                ["value"] = value
            }
        }.ToMessage();
        var restored = message.ToFlowEvent();
        restored.ShouldBeSameAs(message.Value);
        restored.Details.ShouldBeSameAs(value);
        restored.Attributes["value"].ShouldBeSameAs(value);
        value.Calls.ShouldBe(0);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => message.Value.Json)));
        results.ShouldAllBe(json => json.Equals(results[0]));
        var json = results[0];
        json.GetProperty("Attributes").GetProperty("value").GetString().ShouldBe("hello");
        restored.Details.Json.GetString().ShouldBe("hello");
        value.Calls.ShouldBe(1);
        var restoredMessage = JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize(message))!;
        restoredMessage.CorrelationId.ShouldBe(message.CorrelationId);
        restoredMessage.ToFlowEvent().Attributes["value"].ShouldBe("hello");
        restoredMessage.ToFlowEvent().Details.Json.GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Concurrent_json_readers_share_one_conversion_and_owned_result()
    {
        var value = new TextValue("hello");
        value.Text.ShouldBe("hello");
        value.Calls.ShouldBe(0);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => value.Json)));
        value.Calls.ShouldBe(1);
        results.ShouldAllBe(json => json.GetString() == "hello");
        results.ShouldAllBe(json => json.Equals(results[0]));
    }

    [Fact]
    public void Dynamic_value_owns_input_before_first_lazy_read()
    {
        FlowValue value;
        using (var document = JsonDocument.Parse("{\"count\":42}"))
            value = new JsonFlowValue(document.RootElement);
        value.Json.GetProperty("count").GetInt32().ShouldBe(42);
    }

    [Fact]
    public async Task Typed_processing_and_forwarding_do_not_force_json()
    {
        var value = new TextValue("hello");
        await using var node = new Probe(input =>
        {
            ((TextValue)input).Text.ShouldBe("hello");
            return input;
        });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)value));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).Value.ShouldBeSameAs(value);
        value.Calls.ShouldBe(0);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Lazy_conversion_failure_is_cached_and_does_not_stop_following_input()
    {
        var exception = new JsonException("Cannot represent this value.");
        var bad = new FailingValue(exception);
        Should.Throw<JsonException>(() => bad.Json).ShouldBeSameAs(exception);
        Should.Throw<JsonException>(() => bad.Json).ShouldBeSameAs(exception);
        bad.Calls.ShouldBe(1);
        await using var node = new Probe(input => new JsonFlowValue(input.Json));
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)bad));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new TextValue("next")));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).Value!.Json.GetString().ShouldBe("next");
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Error_is_a_value_and_serializes_as_portable_data_without_the_json_cache()
    {
        FlowValue error = new FlowError("invalid", "Invalid input", "Test");
        var failure = FlowMessage.Create((FlowError)error);
        failure.Value.ShouldBeSameAs(error);
        failure.Error.ShouldBeSameAs(error);
        FlowMessage.Create(error).IsError.ShouldBeTrue();
        // A structured error document can still be carried as ordinary business data.
        var ordinary = FlowMessage.Create(new JsonFlowValue(error.Json));
        ordinary.IsError.ShouldBeFalse();
        ordinary.Error.ShouldBeNull();
        ordinary.Value.Json.GetProperty("Code").GetString().ShouldBe("invalid");
        error.Json.GetProperty("Code").GetString().ShouldBe("invalid");
        error.Json.TryGetProperty("Json", out _).ShouldBeFalse();
        var wire = JsonSerializer.Serialize(error);
        var restored = JsonSerializer.Deserialize<FlowValue>(wire)!;
        restored.ShouldBeOfType<JsonFlowValue>();
        restored.Json.GetProperty("Category").GetString().ShouldBe("Test");
        JsonSerializer.Serialize(restored).ShouldBe(wire);
    }

    [Fact]
    public void Undefined_representation_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new JsonFlowValue(default));
        Should.Throw<InvalidOperationException>(() => new UndefinedValue().Json);
    }

    private sealed class TextValue(string text) : FlowValue
    {
        public string Text { get; } = text;

        public int Calls;
        protected override JsonElement CreateJson()
        {
            Interlocked.Increment(ref Calls);
            return JsonSerializer.SerializeToElement(Text);
        }
    }

    private sealed class FailingValue(Exception exception) : FlowValue
    {
        public int Calls;
        protected override JsonElement CreateJson()
        {
            Calls++;
            throw exception;
        }
    }

    private sealed class UndefinedValue : FlowValue
    {
        protected override JsonElement CreateJson() => default;
    }

    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Probe(Func<FlowValue, FlowValue> process)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.process = process;
            Lifetime.Observe();
        }

        private readonly Func<FlowValue, FlowValue> process;
        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([message.WithValue(process(message.Value))]);
    }
}
