using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationSignalFanoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Signal_links_observe_only_messages_accepted_during_their_registration()
    {
        await using var signals = new ApplicationRuntimeSignals(null);
        var held = new HeldTarget();
        using var heldLink = signals.SystemEvents.LinkTo(held);
        var early = new BufferBlock<FlowMessage>();
        using var earlyLink = signals.SystemEvents.LinkTo(early);
        var messages = Enumerable.Range(1, 4).Select(index => Event($"event-{index}")).ToArray();
        (await signals.PublishSystemEventAsync(messages[0], default)).IsAccepted.ShouldBeTrue();
        await held.Offered.Task.WaitAsync(Timeout);
        foreach (var message in messages.Skip(1).Take(2))
            (await signals.PublishSystemEventAsync(message, default)).IsAccepted.ShouldBeTrue();
        var late = new BufferBlock<FlowMessage>();
        using var lateLink = signals.SystemEvents.LinkTo(late);
        heldLink.Dispose();
        (await signals.PublishSystemEventAsync(messages[3], default)).IsAccepted.ShouldBeTrue();
        signals.Complete();
        await signals.Completion.WaitAsync(Timeout);

        foreach (var message in messages)
            (await early.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        (await late.ReceiveAsync(Timeout)).ShouldBeSameAs(messages[3]);
        late.TryReceive(out _).ShouldBeFalse();
        early.TryReceive(out _).ShouldBeFalse();
        held.Values.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Signal_filters_and_message_limits_preserve_order_and_identity()
    {
        await using var signals = new ApplicationRuntimeSignals(null);
        var all = new BufferBlock<FlowMessage>();
        var selected = new BufferBlock<FlowMessage>();
        using var allLink = signals.SystemEvents.LinkTo(all);
        using var selectedLink = signals.SystemEvents.LinkTo(selected,
            new DataflowLinkOptions { MaxMessages = 2, PropagateCompletion = true },
            message => int.Parse(message.ToFlowEvent().Name) % 2 == 0);
        var messages = Enumerable.Range(1, 6).Select(index => Event(index.ToString())).ToArray();

        foreach (var message in messages)
            (await signals.PublishSystemEventAsync(message, default)).IsAccepted.ShouldBeTrue();
        signals.Complete();
        await signals.Completion.WaitAsync(Timeout);

        foreach (var expected in messages)
            (await all.ReceiveAsync(Timeout)).ShouldBeSameAs(expected);
        (await selected.ReceiveAsync(Timeout)).ShouldBeSameAs(messages[1]);
        (await selected.ReceiveAsync(Timeout)).ShouldBeSameAs(messages[3]);
        selected.TryReceive(out _).ShouldBeFalse();
        selected.Completion.IsCompleted.ShouldBeFalse(); // MaxMessages detached it before completion.
        all.TryReceive(out _).ShouldBeFalse();
        signals.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Signal_subscriber_failure_is_reported_once_without_interrupting_siblings(
        bool throws, bool observerThrows)
    {
        await using var signals = new ApplicationRuntimeSignals(null);
        var failure = new InvalidOperationException("subscriber failed");
        var failing = new CallbackTarget(offerFailure: failure, decline: !throws);
        var healthy = new BufferBlock<FlowMessage>();
        var diagnostics = new BufferBlock<FlowMessage>();
        var reports = new ConcurrentQueue<FlowMessage>();
        signals.EventObserver = message =>
        {
            if (message.ToFlowEvent().Name != ApplicationDiagnosticNames.SystemEventDeliveryFailed)
                return;
            reports.Enqueue(message);
            if (observerThrows)
                throw new InvalidOperationException("observer failed");
        };
        using var failedLink = signals.SystemEvents.LinkTo(failing);
        using var healthyLink = signals.SystemEvents.LinkTo(healthy);
        using var diagnosticLink = signals.Diagnostics.LinkTo(diagnostics);
        var messages = Enumerable.Range(1, 3).Select(index => Event($"event-{index}")).ToArray();

        foreach (var message in messages)
        {
            (await signals.PublishSystemEventAsync(message, default)).IsAccepted.ShouldBeTrue();
            (await healthy.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        }
        signals.Complete();
        await signals.Completion.WaitAsync(Timeout);

        failing.Offers.ShouldBe(1);
        reports.Count.ShouldBe(1);
        var report = reports.Single();
        var error = report.ToFlowEvent().Details.Json.Deserialize<FlowError>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        error.Code.ShouldBe("runtime.system_event.delivery_failed");
        if (throws)
            error.Message.ShouldBe(failure.Message);
        if (!observerThrows)
            (await diagnostics.ReceiveAsync(Timeout)).ShouldBeSameAs(report);
        diagnostics.TryReceive(out _).ShouldBeFalse();
        healthy.TryReceive(out _).ShouldBeFalse();
        signals.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signal_reservation_round_trip_uses_the_exposed_source(bool releaseFirst)
    {
        await using var signals = new ApplicationRuntimeSignals(null);
        var target = new HeldTarget();
        using var link = signals.SystemEvents.LinkTo(target);
        var expected = Event("reserved");
        (await signals.PublishSystemEventAsync(expected, default)).IsAccepted.ShouldBeTrue();
        var offer = await target.Offered.Task.WaitAsync(Timeout);

        offer.Source.ShouldBeSameAs(signals.SystemEvents);
        offer.Source.ReserveMessage(offer.Header, target).ShouldBeTrue();
        if (releaseFirst)
        {
            offer.Source.ReleaseReservation(offer.Header, target);
            offer.Source.ReserveMessage(offer.Header, target).ShouldBeTrue();
        }
        offer.Source.ConsumeMessage(offer.Header, target, out var consumed).ShouldBeSameAs(expected);
        consumed.ShouldBeTrue();
        signals.Complete();
        await signals.Completion.WaitAsync(Timeout);
        offer.Source.ReserveMessage(offer.Header, target).ShouldBeFalse();
        offer.Source.ConsumeMessage(offer.Header, target, out consumed).ShouldBeNull();
        consumed.ShouldBeFalse();
        target.Completion.IsCompleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signal_terminal_callbacks_are_isolated_and_preserve_the_original_outcome(bool fault)
    {
        await using var signals = new ApplicationRuntimeSignals(null);
        var original = new InvalidOperationException("original source failure");
        var failing = new CallbackTarget(terminalFailure: new InvalidOperationException("callback failed"));
        var healthy = new CallbackTarget();
        using var failingLink = signals.SystemEvents.LinkTo(failing, new() { PropagateCompletion = true });
        using var healthyLink = signals.SystemEvents.LinkTo(healthy, new() { PropagateCompletion = true });
        var source = signals.SystemEvents;
        if (fault)
        {
            source.Fault(original);
            (await Should.ThrowAsync<InvalidOperationException>(async () =>
                await source.Completion.WaitAsync(Timeout))).ShouldBeSameAs(original);
            (await Should.ThrowAsync<InvalidOperationException>(async () =>
                await healthy.Completion.WaitAsync(Timeout))).ShouldBeSameAs(original);
            failing.FaultCalls.ShouldBe(1);
            healthy.FaultCalls.ShouldBe(1);
            failing.CompleteCalls.ShouldBe(0);
        }
        else
        {
            source.Complete();
            await source.Completion.WaitAsync(Timeout);
            await healthy.Completion.WaitAsync(Timeout);
            failing.CompleteCalls.ShouldBe(1);
            healthy.CompleteCalls.ShouldBe(1);
            failing.FaultCalls.ShouldBe(0);
        }
        signals.Complete();
        if (fault)
            (await Should.ThrowAsync<InvalidOperationException>(async () =>
                await signals.Completion.WaitAsync(Timeout))).ShouldBeSameAs(original);
        else
            await signals.Completion.WaitAsync(Timeout);
        (await signals.PublishSystemEventAsync(Event("late"), default)).Status
            .ShouldBe(SystemEventPublishStatus.Completed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Signal_disposal_releases_all_streams_and_blocked_publication(bool synchronous, bool reserve)
    {
        var signals = new ApplicationRuntimeSignals(null);
        var sources = new[] { signals.SystemEvents, signals.Diagnostics, signals.Activity };
        var targets = sources.Select(_ => new HeldTarget()).ToArray();
        var links = sources.Select((source, index) => source.LinkTo(targets[index],
            new DataflowLinkOptions { PropagateCompletion = true })).ToArray();
        Task? disposal = null;
        var reservations = new List<(HeldTarget Target, HeldOffer Offer)>();
        try
        {
            (await signals.PublishSystemEventAsync(Event("active"), default)).IsAccepted.ShouldBeTrue();
            signals.TryPublishDiagnostic(Diagnostic("active-diagnostic")).ShouldBeTrue();
            for (var index = 0; index < targets.Length; index++)
            {
                var offer = await targets[index].Offered.Task.WaitAsync(Timeout);
                offer.Source.ShouldBeSameAs(sources[index]);
                if (reserve)
                {
                    offer.Source.ReserveMessage(offer.Header, targets[index]).ShouldBeTrue();
                    reservations.Add((targets[index], offer));
                }
            }
            for (var index = 0; index < ApplicationRuntimeSignals.Capacity; index++)
            {
                (await signals.PublishSystemEventAsync(Event($"queued-{index}"), default)).IsAccepted.ShouldBeTrue();
                signals.TryPublishDiagnostic(Diagnostic($"queued-diagnostic-{index}")).ShouldBeTrue();
            }
            signals.TryPublishDiagnostic(Diagnostic("overflow")).ShouldBeFalse();
            var blocked = signals.PublishSystemEventAsync(Event("blocked"), default).AsTask();
            blocked.IsCompleted.ShouldBeFalse();
            disposal = synchronous ? Task.Run(signals.Dispose) : signals.DisposeAsync().AsTask();
            (await blocked.WaitAsync(Timeout)).Status.ShouldBe(SystemEventPublishStatus.Completed);
            if (reserve)
                disposal.IsCompleted.ShouldBeFalse(); // Native reservations remain target-owned.
            foreach (var (target, offer) in reservations)
                offer.Source.ReleaseReservation(offer.Header, target);
            reservations.Clear();
            await disposal.WaitAsync(Timeout);
            await signals.Completion.WaitAsync(Timeout);

            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                // Releasing a reservation may reoffer the same active message.
                // No queued message may be dispatched after the stream is aborted.
                target.Values.ShouldNotBeEmpty();
                foreach (var offered in target.Values)
                    offered.ToFlowEvent().Name.ShouldBe(index == 1 ? "active-diagnostic" : "active");
                target.Completion.IsCompleted.ShouldBeFalse();
            }
            signals.TryPublishDiagnostic(Diagnostic("late")).ShouldBeFalse();
            (await signals.PublishSystemEventAsync(Event("late"), default)).Status
                .ShouldBe(SystemEventPublishStatus.Completed);
            await signals.DisposeAsync();
        }
        finally
        {
            foreach (var (target, offer) in reservations)
                offer.Source.ReleaseReservation(offer.Header, target);
            foreach (var link in links)
                link.Dispose();
            if (disposal is not null)
                await disposal.WaitAsync(Timeout);
            await signals.DisposeAsync().AsTask().WaitAsync(Timeout);
        }
    }

    private static FlowMessage Event(string name)
        => new FlowEvent { Name = name, Kind = FlowEventKind.Lifecycle }.ToMessage();

    private static FlowMessage Diagnostic(string name)
        => new FlowEvent { Name = name, Kind = FlowEventKind.Diagnostic }.ToMessage();

    private sealed record HeldOffer(ISourceBlock<FlowMessage> Source, DataflowMessageHeader Header);

    private sealed class HeldTarget : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HeldOffer> Offered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Offers { get; private set; }
        public ConcurrentQueue<FlowMessage> Values { get; } = new();
        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            if (source is null)
                return DataflowMessageStatus.Declined;
            Offers++;
            Values.Enqueue(value);
            Offered.TrySetResult(new(source, header));
            return DataflowMessageStatus.Postponed;
        }
        public void Complete() => _completion.TrySetResult();
        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class CallbackTarget(Exception? offerFailure = null, bool decline = false,
        Exception? terminalFailure = null) : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => _completion.Task;
        public int Offers { get; private set; }
        public int CompleteCalls { get; private set; }
        public int FaultCalls { get; private set; }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            Offers++;
            if (decline)
                return DataflowMessageStatus.DecliningPermanently;
            if (offerFailure is not null)
                throw offerFailure;
            if (consumeToAccept)
            {
                source!.ConsumeMessage(header, this, out var consumed);
                if (!consumed)
                    return DataflowMessageStatus.NotAvailable;
            }
            return DataflowMessageStatus.Accepted;
        }
        public void Complete()
        {
            CompleteCalls++;
            if (terminalFailure is not null) throw terminalFailure;
            _completion.TrySetResult();
        }
        public void Fault(Exception exception)
        {
            FaultCalls++;
            if (terminalFailure is not null) throw terminalFailure;
            _completion.TrySetException(exception);
        }
    }
}
