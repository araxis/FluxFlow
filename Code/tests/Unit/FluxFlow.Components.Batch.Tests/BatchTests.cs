using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Batch.Tests;

public sealed class BatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Size_flushes_full_batch_and_completion_flushes_remainder_without_serialization()
    {
        await using var batch = new Batch(new() { BatchSize = 3, MaxWait = TimeSpan.FromHours(1) });
        var sink = Sink(batch);
        var messages = Enumerable.Range(0, 5).Select(_ => FlowMessage.Create((FlowValue)new NoJson(), CorrelationId.New())).ToArray();
        foreach (var message in messages) await batch.Input.SendAsync(message);
        batch.Complete();
        var first = await sink.ReceiveAsync(Deadline);
        var second = await sink.ReceiveAsync(Deadline);
        var items = ((FlowBatch)first.Value).Items.Concat(((FlowBatch)second.Value).Items).ToArray();
        ((FlowBatch)first.Value).Items.Count.ShouldBe(3);
        ((FlowBatch)second.Value).Items.Count.ShouldBe(2);
        for (var i = 0; i < items.Length; i++) items[i].ShouldBeSameAs(messages[i]);
        first.CorrelationId.ShouldBeNull();
        first.CausationId.ShouldBeNull();
        first.TraceId.ShouldNotBe(messages[0].TraceId);
        await batch.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Deadline_starts_with_first_item_and_later_items_do_not_restart_it()
    {
        var time = new FakeTimeProvider();
        await using var batch = new Batch(new() { BatchSize = 3, MaxWait = TimeSpan.FromSeconds(5), TimeProvider = time });
        var sink = Sink(batch);
        var first = FlowMessage.Create((FlowValue)"one");
        var second = FlowMessage.Create((FlowValue)"two");
        await batch.Input.SendAsync(first);
        await Fence(batch, sink);
        time.Advance(TimeSpan.FromSeconds(4));
        await batch.Input.SendAsync(second);
        await Fence(batch, sink);
        time.Advance(TimeSpan.FromSeconds(1));
        var result = (FlowBatch)(await sink.ReceiveAsync(Deadline)).Value;
        result.Items.Count.ShouldBe(2);
        result.Items[0].ShouldBeSameAs(first);
        result.Items[1].ShouldBeSameAs(second);
        time.Advance(TimeSpan.FromDays(1));
        await Fence(batch, sink); // idle time produces no empty batches
        batch.Complete();
        await batch.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Full_batch_cancels_old_timer_and_next_batch_has_its_own_deadline()
    {
        var time = new FakeTimeProvider();
        await using var batch = new Batch(new() { BatchSize = 2, MaxWait = TimeSpan.FromSeconds(5), TimeProvider = time });
        var sink = Sink(batch);
        await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"a"));
        await Fence(batch, sink);
        time.Advance(TimeSpan.FromSeconds(4));
        await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"b"));
        ((FlowBatch)(await sink.ReceiveAsync(Deadline)).Value).Items.Count.ShouldBe(2);
        await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"c"));
        await Fence(batch, sink);
        time.Advance(TimeSpan.FromSeconds(1));
        await Fence(batch, sink); // original timer must not flush c
        time.Advance(TimeSpan.FromSeconds(4));
        ((FlowBatch)(await sink.ReceiveAsync(Deadline)).Value).Items.Single().Value.Json.GetString().ShouldBe("c");
        batch.Complete();
        await batch.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Timer_and_size_competition_preserves_all_messages_once_in_order()
    {
        await using var batch = new Batch(new() { BatchSize = 3, MaxWait = TimeSpan.FromMilliseconds(2), OutputCapacity = 1 });
        var sink = Sink(batch);
        var inputs = Enumerable.Range(0, 80).Select(i => FlowMessage.Create((FlowValue)i.ToString())).ToArray();
        foreach (var message in inputs)
        {
            await batch.Input.SendAsync(message);
            await Task.Delay(1);
        }
        batch.Complete();
        await batch.Completion.WaitAsync(Deadline);
        var actual = new List<FlowMessage>();
        while (sink.TryReceive(out var message))
        {
            var items = ((FlowBatch)message.Value).Items;
            items.Count.ShouldBeInRange(1, 3);
            actual.AddRange(items);
        }
        actual.Count.ShouldBe(inputs.Length);
        for (var i = 0; i < inputs.Length; i++) actual[i].ShouldBeSameAs(inputs[i]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deadline_and_graceful_completion_flush_the_same_partial_batch_once(bool deadlineFirst)
    {
        var time = new FakeTimeProvider();
        var wait = TimeSpan.FromSeconds(5);
        var lastTick = TimeSpan.FromTicks(1);
        await using var batch = new Batch(new() { BatchSize = 3, MaxWait = wait, TimeProvider = time });
        var sink = Sink(batch);
        var first = FlowMessage.Create((FlowValue)"first", CorrelationId.New());
        var second = FlowMessage.Create((FlowValue)"second", CorrelationId.New());
        (await batch.Input.SendAsync(first)).ShouldBeTrue();
        (await batch.Input.SendAsync(second)).ShouldBeTrue();
        await Fence(batch, sink);
        time.Advance(wait - lastTick);

        // Start both flush paths without waiting for the first one to finish.
        if (deadlineFirst)
        {
            time.Advance(lastTick);
            batch.Complete();
        }
        else
        {
            batch.Complete();
            time.Advance(lastTick);
        }

        await batch.Completion.WaitAsync(Deadline);
        var envelope = await sink.ReceiveAsync(Deadline);
        envelope.CorrelationId.ShouldBeNull();
        var items = envelope.Value.ShouldBeOfType<FlowBatch>().Items;
        items.Count.ShouldBe(2);
        items[0].ShouldBeSameAs(first);
        items[1].ShouldBeSameAs(second);
        (await sink.OutputAvailableAsync().WaitAsync(Deadline)).ShouldBeFalse();
    }

    [Fact]
    public async Task Stopped_runs_deadline_cannot_flush_a_fresh_runs_partial_batch()
    {
        var time = new FakeTimeProvider();
        var options = new BatchOptions { BatchSize = 3, MaxWait = TimeSpan.FromSeconds(5), TimeProvider = time };
        await using (var old = new Batch(options))
        {
            var sink = Sink(old);
            var previous = FlowMessage.Create((FlowValue)"previous", CorrelationId.New());
            (await old.Input.SendAsync(previous)).ShouldBeTrue();
            await Fence(old, sink);
            time.Advance(TimeSpan.FromSeconds(4));
            old.Complete();
            await old.Completion.WaitAsync(Deadline);
            (await sink.ReceiveAsync(Deadline)).Value.ShouldBeOfType<FlowBatch>().Items.Single().ShouldBeSameAs(previous);
            (await sink.OutputAvailableAsync().WaitAsync(Deadline)).ShouldBeFalse();
        }

        await using var fresh = new Batch(options);
        var downstream = Sink(fresh);
        var current = FlowMessage.Create((FlowValue)"current", CorrelationId.New());
        (await fresh.Input.SendAsync(current)).ShouldBeTrue();
        await Fence(fresh, downstream);
        time.Advance(TimeSpan.FromSeconds(1)); // The first run's deadline.
        await Fence(fresh, downstream);
        time.Advance(TimeSpan.FromSeconds(4)); // The fresh run's deadline.
        (await downstream.ReceiveAsync(Deadline)).Value.ShouldBeOfType<FlowBatch>().Items.Single().ShouldBeSameAs(current);
        fresh.Complete();
        await fresh.Completion.WaitAsync(Deadline);
        (await downstream.OutputAvailableAsync().WaitAsync(Deadline)).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_discards_partial_batch_and_stops_timer()
    {
        var time = new FakeTimeProvider();
        using var stopped = new CancellationTokenSource();
        await using var batch = new Batch(new()
        { BatchSize = 3, MaxWait = TimeSpan.FromSeconds(5), TimeProvider = time, Execution = new() { CancellationToken = stopped.Token } });
        var sink = Sink(batch);
        await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"pending"));
        await Fence(batch, sink);
        stopped.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => batch.Completion.WaitAsync(Deadline));
        await batch.DisposeAsync();
        time.Advance(TimeSpan.FromHours(1));
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Full_consumer_does_not_block_native_broadcast_publication()
    {
        await using var batch = new Batch(new()
        { BatchSize = 1, MaxWait = TimeSpan.FromHours(1), OutputCapacity = 1, Execution = new() { BoundedCapacity = 1 } });
        var fullTarget = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var occupied = FlowMessage.Create((FlowValue)"occupied");
        fullTarget.Post(occupied).ShouldBeTrue();
        using var link = batch.Output.LinkTo(fullTarget);
        var sends = Enumerable.Range(0, 16).Select(i => batch.Input.SendAsync(FlowMessage.Create((FlowValue)i.ToString()))).ToArray();
        (await Task.WhenAll(sends).WaitAsync(Deadline)).ShouldAllBe(accepted => accepted);
        batch.Complete();
        await batch.Completion.WaitAsync(Deadline);
        fullTarget.Count.ShouldBe(1);
        link.Dispose();
        (await fullTarget.ReceiveAsync(Deadline)).ShouldBeSameAs(occupied);
    }

    [Fact]
    public async Task Explicit_fault_closes_batch_admission_and_preserves_failure()
    {
        await using var batch = new Batch(new() { BatchSize = 2, MaxWait = TimeSpan.FromHours(1) });
        var sink = Sink(batch);
        await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"pending"));
        await Fence(batch, sink);
        var failure = new InvalidOperationException("abort");
        batch.Fault(failure);
        (await batch.Input.SendAsync(FlowMessage.Create((FlowValue)"too late"))).ShouldBeFalse();
        (await Should.ThrowAsync<InvalidOperationException>(() => batch.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public void Payload_snapshots_list_and_json_preserves_envelopes()
    {
        var original = FlowMessage.Create((FlowValue)"value", CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["key"] = "kept" });
        var list = new List<FlowMessage> { original };
        var payload = new FlowBatch(list);
        list.Clear();
        payload.Items.Single().ShouldBeSameAs(original);
        var json = payload.Json;
        json.GetArrayLength().ShouldBe(1);
        json[0].GetProperty("Value").GetString().ShouldBe("value");
        json[0].GetProperty("Headers").GetProperty("key").GetString().ShouldBe("kept");
        json[0].GetProperty("CorrelationId").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
        Should.Throw<NotSupportedException>(() => ((IList<FlowMessage>)payload.Items).Clear());
    }

    [Fact]
    public async Task Empty_completion_does_not_emit_and_invalid_settings_are_rejected()
    {
        await using var batch = new Batch(new() { BatchSize = 2, MaxWait = TimeSpan.FromSeconds(1) });
        var sink = Sink(batch);
        batch.Complete();
        await batch.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        Should.Throw<ArgumentOutOfRangeException>(() => new Batch(new() { BatchSize = 0, MaxWait = TimeSpan.FromSeconds(1) }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Batch(new() { BatchSize = 1, MaxWait = TimeSpan.Zero }));
        Should.Throw<ArgumentException>(() => new Batch(new() { BatchSize = 2, MaxWait = TimeSpan.FromSeconds(1), Execution = new() { MaxDegreeOfParallelism = 2 } }));
    }

    // An error bypass on the serial input confirms that earlier ordinary inputs were processed.
    private static async Task Fence(Batch batch, BufferBlock<FlowMessage> sink)
    {
        var error = FlowMessage.Create((FlowValue)new FlowError("marker", "marker", "test"));
        await batch.Input.SendAsync(error);
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
    }

    private static BufferBlock<FlowMessage> Sink(Batch batch)
    {
        var sink = new BufferBlock<FlowMessage>();
        batch.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    private sealed class NoJson : FlowValue
    {
        protected override System.Text.Json.JsonElement CreateJson() => throw new Exception("Batch must not serialize while collecting.");
    }
}
