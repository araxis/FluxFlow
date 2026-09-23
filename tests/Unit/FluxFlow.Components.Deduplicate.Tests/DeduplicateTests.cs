using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Deduplicate.Tests;

public sealed class DeduplicateTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static string Select(FlowMessage message) => message.Value.Json.GetString()!;

    [Fact]
    public async Task Nonconsecutive_duplicates_use_configured_comparer_and_preserve_identity()
    {
        await using var filter = new Deduplicate<string>(new()
        { KeySelector = Select, Comparer = StringComparer.OrdinalIgnoreCase, Retention = TimeSpan.FromMinutes(1) });
        var sink = Sink(filter);
        var inputs = new[] { "A", "a", "B", "A" }.Select(s => FlowMessage.Create((FlowValue)s)).ToArray();
        foreach (var message in inputs) await filter.Input.SendAsync(message);
        filter.Complete();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(inputs[0]);
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(inputs[2]);
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Retention_expires_at_boundary_and_duplicates_do_not_extend_it()
    {
        var clock = new FakeTimeProvider();
        await using var filter = new Deduplicate<string>(new()
        { KeySelector = Select, Retention = TimeSpan.FromSeconds(5), TimeProvider = clock });
        var sink = Sink(filter);
        await filter.Input.SendAsync(FlowMessage.Create((FlowValue)"A"));
        await sink.ReceiveAsync(Deadline);
        clock.Advance(TimeSpan.FromSeconds(4));
        await filter.Input.SendAsync(FlowMessage.Create((FlowValue)"A"));
        var marker = FlowMessage.Create((FlowValue)new FlowError("marker", "marker", "test"));
        await filter.Input.SendAsync(marker);
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(marker);
        clock.Advance(TimeSpan.FromSeconds(1));
        var afterExpiry = FlowMessage.Create((FlowValue)"A");
        await filter.Input.SendAsync(afterExpiry);
        filter.Complete();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(afterExpiry);
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Capacity_evicts_oldest_forwarded_key_without_refreshing_duplicates()
    {
        await using var filter = new Deduplicate<string>(new()
        { KeySelector = Select, Retention = TimeSpan.FromHours(1), MaxKeys = 2 });
        var sink = Sink(filter);
        foreach (var key in new[] { "A", "B", "A", "C", "A" })
            await filter.Input.SendAsync(FlowMessage.Create((FlowValue)key));
        filter.Complete();
        foreach (var expected in new[] { "A", "B", "C", "A" })
            Select(await sink.ReceiveAsync(Deadline)).ShouldBe(expected);
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Tuple_keys_and_selector_failure_do_not_poison_future_messages()
    {
        await using var filter = new Deduplicate<(string Device, string Event)>(new()
        {
            KeySelector = m => Select(m) == "!" ? throw new InvalidOperationException("bad key") : ("device", Select(m)),
            Retention = TimeSpan.FromMinutes(1)
        });
        var sink = Sink(filter);
        foreach (var key in new[] { "A", "!", "A", "B" })
            await filter.Input.SendAsync(FlowMessage.Create((FlowValue)key));
        filter.Complete();
        Select(await sink.ReceiveAsync(Deadline)).ShouldBe("A");
        (await sink.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        Select(await sink.ReceiveAsync(Deadline)).ShouldBe("B");
        await filter.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new Deduplicate<string>(null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new Deduplicate<string>(new()
        { KeySelector = Select, Retention = TimeSpan.Zero }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Deduplicate<string>(new()
        { KeySelector = Select, Retention = TimeSpan.FromSeconds(1), MaxKeys = 0 }));
        Should.Throw<ArgumentException>(() => new Deduplicate<string>(new()
        { KeySelector = Select, Retention = TimeSpan.FromSeconds(1), Execution = new() { EnsureOrdered = false } }));
    }

    private static BufferBlock<FlowMessage> Sink<TKey>(Deduplicate<TKey> filter) where TKey : notnull
    {
        var sink = new BufferBlock<FlowMessage>();
        filter.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
}
