using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.ChangeFilter.Tests;

public sealed class ChangeFilterTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Only_consecutive_equal_keys_are_suppressed_and_messages_are_not_rebuilt()
    {
        await using var filter = new ChangeFilter<string>(new()
        {
            KeySelector = m => m.Headers["key"].Json.GetString()!,
            AreEqual = StringComparer.OrdinalIgnoreCase.Equals
        });
        var sink = Sink(filter);
        var inputs = new[] { "A", "a", "B", "A" }.Select(Keyed).ToArray();
        foreach (var message in inputs) await filter.Input.SendAsync(message);
        filter.Complete();
        foreach (var index in new[] { 0, 2, 3 })
            (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(inputs[index]);
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Approximate_comparison_uses_last_forwarded_value_not_last_received_value()
    {
        await using var filter = new ChangeFilter<double>(new()
        { KeySelector = m => m.Value.Json.GetDouble(), AreEqual = (a, b) => Math.Abs(a - b) < 0.5 });
        var sink = Sink(filter);
        foreach (var value in new[] { 20.0, 20.2, 20.4, 20.6 })
            await filter.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value))));
        filter.Complete();
        (await sink.ReceiveAsync(Deadline)).Value.Json.GetDouble().ShouldBe(20.0);
        (await sink.ReceiveAsync(Deadline)).Value.Json.GetDouble().ShouldBe(20.6);
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selection_or_comparison_errors_do_not_replace_previous_key(bool compareFails)
    {
        await using var filter = new ChangeFilter<string>(new()
        {
            KeySelector = m =>
            {
                var key = m.Headers["key"].Json.GetString()!;
                return key == "!" && !compareFails ? throw new InvalidOperationException("select") : key;
            },
            AreEqual = (a, b) => b == "!" ? throw new InvalidOperationException("compare") : a == b
        });
        var sink = Sink(filter);
        var first = Keyed("A");
        var error = FlowMessage.Create((FlowValue)new FlowError("test", "test", "test"));
        foreach (var message in new[] { first, error, Keyed("!"), Keyed("A"), Keyed("B") })
            await filter.Input.SendAsync(message);
        filter.Complete();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(first);
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        (await sink.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).Headers["key"].Json.GetString().ShouldBe("B");
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new ChangeFilter<string>(null!));
        Should.Throw<ArgumentNullException>(() => new ChangeFilter<string>(new() { KeySelector = null! }));
        Should.Throw<ArgumentException>(() => new ChangeFilter<string>(new()
        { KeySelector = _ => "a", Execution = new() { MaxDegreeOfParallelism = 2 } }));
    }

    private static FlowMessage Keyed(string key) => FlowMessage.Create((FlowValue)new NoJson(),
        headers: new Dictionary<string, FlowValue> { ["key"] = key });

    private static BufferBlock<FlowMessage> Sink(ChangeFilter<string> filter) => Sink<string>(filter);
    private static BufferBlock<FlowMessage> Sink<TKey>(ChangeFilter<TKey> filter) where TKey : notnull
    {
        var sink = new BufferBlock<FlowMessage>();
        filter.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
    private sealed class NoJson : FlowValue
    {
        protected override JsonElement CreateJson() => throw new Exception("Selector did not request payload JSON.");
    }
}
