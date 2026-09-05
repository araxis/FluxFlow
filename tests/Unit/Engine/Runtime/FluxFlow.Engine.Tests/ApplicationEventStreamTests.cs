using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Events;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationEventStreamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arbitrary_event_payload_preserves_identity_and_context_without_report_registration(bool isError)
    {
        await using var stream = new ApplicationEventStream(new() { Application = "orders" });
        var target = new BufferBlock<FlowMessage>();
        using var link = stream.Events.LinkTo(target);
        var headers = new Dictionary<string, string>
        {
            [FlowEventHeaders.Type] = "custom.vendor-neutral.event",
            [FlowEventHeaders.Workflow] = "Main",
            [FlowEventHeaders.Component] = "Source",
            [FlowEventHeaders.Source] = "Main.Source.Events",
            ["custom.schema"] = "any-value"
        };
        var correlation = CorrelationId.New();
        var causation = MessageId.New();
        var original = isError
            ? FlowMessage.CreateError(new FlowError("custom.failure", "Failure", "custom", false), correlationId: correlation, headers: headers, causationId: causation)
            : FlowMessage.Create(FlowValue.From(new { items = new[] { new { label = "custom", amount = 12 } } }), correlationId: correlation, headers: headers, causationId: causation);

        stream.Publish(original, "rev-7");
        var actual = await target.ReceiveAsync(TimeSpan.FromSeconds(5));

        actual.MessageId.ShouldBe(original.MessageId);
        actual.TraceId.ShouldBe(original.TraceId);
        actual.Timestamp.ShouldBe(original.Timestamp);
        actual.CorrelationId.ShouldBe(original.CorrelationId);
        actual.CausationId.ShouldBe(original.CausationId);
        actual.IsError.ShouldBe(isError);
        actual.Headers[FlowEventHeaders.Application].ShouldBe("orders");
        actual.Headers[FlowEventHeaders.Revision].ShouldBe("rev-7");
        foreach (var pair in headers) actual.Headers[pair.Key].ShouldBe(pair.Value);
        if (isError) actual.Error!.Code.ShouldBe("custom.failure");
        else actual.Value!.ToJsonElement().GetProperty("items")[0].GetProperty("amount").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task Native_fanout_preserves_accepted_order_and_propagates_completion()
    {
        await using var stream = new ApplicationEventStream(new());
        var first = new BufferBlock<FlowMessage>();
        var second = new BufferBlock<FlowMessage>();
        using var firstLink = stream.Events.LinkTo(first, new() { PropagateCompletion = true });
        using var secondLink = stream.Events.LinkTo(second, new() { PropagateCompletion = true });
        for (var i = 0; i < 5; i++) stream.Publish(Event(i));
        stream.Complete();
        await stream.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var firstRecords = Drain(first);
        var secondRecords = Drain(second);
        firstRecords.Select(Value).ShouldBe(new double[] { 0, 1, 2, 3, 4 });
        secondRecords.Select(message => message.MessageId).ShouldBe(firstRecords.Select(message => message.MessageId));
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Standard_predicate_and_max_messages_select_without_affecting_other_consumers()
    {
        await using var stream = new ApplicationEventStream(new());
        var selected = new BufferBlock<FlowMessage>();
        var all = new BufferBlock<FlowMessage>();
        using var selectedLink = stream.Events.LinkTo(selected, new() { MaxMessages = 2 }, message => Value(message) > 5);
        using var allLink = stream.Events.LinkTo(all);
        foreach (var value in new[] { 1, 6, 2, 7, 8 }) stream.Publish(Event(value));
        stream.Complete();
        await stream.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain(selected).Select(Value).ShouldBe(new double[] { 6, 7 });
        Drain(all).Select(Value).ShouldBe(new double[] { 1, 6, 2, 7, 8 });
    }

    [Fact]
    public async Task Disposing_one_link_does_not_detach_other_consumers()
    {
        await using var stream = new ApplicationEventStream(new());
        var detached = new BufferBlock<FlowMessage>();
        var remaining = new BufferBlock<FlowMessage>();
        using var detachedLink = stream.Events.LinkTo(detached);
        using var remainingLink = stream.Events.LinkTo(remaining);
        stream.Publish(Event(1));
        Value(await detached.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBe(1);
        Value(await remaining.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBe(1);
        detachedLink.Dispose();
        stream.Publish(Event(2));
        stream.Complete();
        await stream.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain(detached).ShouldBeEmpty();
        Value(Drain(remaining).Single()).ShouldBe(2);
    }

    [Fact]
    public async Task Native_source_fault_propagates_only_to_opted_in_targets()
    {
        var stream = new ApplicationEventStream(new());
        var optedIn = new BufferBlock<FlowMessage>();
        var independent = new BufferBlock<FlowMessage>();
        using var propagatedLink = stream.Events.LinkTo(optedIn, new() { PropagateCompletion = true });
        using var independentLink = stream.Events.LinkTo(independent);
        stream.Events.Fault(new InvalidOperationException("source failed"));
        var sourceFailure = await Should.ThrowAsync<Exception>(async () => await stream.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        sourceFailure.GetBaseException().Message.ShouldBe("source failed");
        var targetFailure = await Should.ThrowAsync<Exception>(async () => await optedIn.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        targetFailure.GetBaseException().ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("source failed");
        independent.Completion.IsCompleted.ShouldBeFalse();
        var disposalFailure = await Should.ThrowAsync<InvalidOperationException>(async () => await stream.DisposeAsync());
        disposalFailure.Message.ShouldBe("source failed");
    }

    [Fact]
    public async Task Bounded_event_ingress_can_drop_without_blocking_the_publisher()
    {
        await using var stream = new ApplicationEventStream(new() { Capacity = 2 });
        using var gate = new GatedTarget();
        var observed = new BufferBlock<FlowMessage>();
        using var gatedLink = stream.Events.LinkTo(gate);
        using var observedLink = stream.Events.LinkTo(observed);
        stream.Publish(Event(0));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(() => { for (var i = 1; i < 100; i++) stream.Publish(Event(i)); })
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { gate.Release.Set(); }
        stream.Complete();
        await stream.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var delivered = Drain(observed);
        delivered.Count.ShouldBeGreaterThan(0);
        delivered.Count.ShouldBeLessThan(100);
        Value(delivered[0]).ShouldBe(0);
    }

    [Fact]
    public async Task Slow_latest_value_consumer_does_not_block_a_ready_consumer()
    {
        await using var stream = new ApplicationEventStream(new());
        var slow = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var fast = new BufferBlock<FlowMessage>();
        using var slowLink = stream.Events.LinkTo(slow);
        using var fastLink = stream.Events.LinkTo(fast);
        for (var i = 1; i <= 8; i++)
        {
            stream.Publish(Event(i));
            Value(await fast.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBe(i);
        }
        slow.Count.ShouldBe(1);
        Value(await slow.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBe(1);
        Value(await slow.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBe(8);
    }

    [Fact]
    public async Task Disposal_finishes_without_waiting_for_a_full_consumer()
    {
        var stream = new ApplicationEventStream(new() { Capacity = 2 });
        var slow = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        using var link = stream.Events.LinkTo(slow);
        for (var i = 0; i < 20; i++) stream.Publish(Event(i));
        await stream.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stream.Events.Completion.IsCompleted.ShouldBeTrue();
        await stream.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65536)]
    public async Task Valid_capacity_boundaries_preserve_producer_context_when_no_application_context_is_configured(int capacity)
    {
        await using var stream = new ApplicationEventStream(new() { Capacity = capacity });
        var target = new BufferBlock<FlowMessage>();
        using var link = stream.Events.LinkTo(target);
        var original = FlowMessage.Create(FlowValue.From("payload"), headers: new Dictionary<string, string>
        {
            [FlowEventHeaders.Application] = "producer-app",
            [FlowEventHeaders.Revision] = "producer-revision"
        });
        stream.Publish(original);
        var actual = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        actual.Headers[FlowEventHeaders.Application].ShouldBe("producer-app");
        actual.Headers[FlowEventHeaders.Revision].ShouldBe("producer-revision");
        actual.MessageId.ShouldBe(original.MessageId);
        actual.Value!.Deserialize<string>().ShouldBe("payload");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Event_capacity_must_be_positive_and_bounded(int capacity)
        => Should.Throw<ArgumentOutOfRangeException>(() => new ApplicationEventStream(new() { Capacity = capacity }));

    internal static FlowMessage Event(int value) => new FlowEvent
    {
        Name = "sample.observed", Kind = FlowEventKind.Domain,
        Measurements = new Dictionary<string, double> { ["value"] = value }
    }.ToMessage();

    private static double Value(FlowMessage message) => message.ToFlowEvent().Measurements["value"];
    private static List<FlowMessage> Drain(BufferBlock<FlowMessage> source)
    {
        var result = new List<FlowMessage>();
        while (source.TryReceive(out var message)) result.Add(message);
        return result;
    }

    private sealed class GatedTarget : ITargetBlock<FlowMessage>, IDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public Task Completion => _completion.Task;
        public void Complete() => _completion.TrySetResult();
        public void Fault(Exception exception) => _completion.TrySetException(exception);
        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) return DataflowMessageStatus.DecliningPermanently;
            if (consumeToAccept)
            {
                source!.ConsumeMessage(header, this, out var consumed);
                if (!consumed) return DataflowMessageStatus.NotAvailable;
            }
            return DataflowMessageStatus.Accepted;
        }
        public void Dispose() => Release.Set();
    }
}
