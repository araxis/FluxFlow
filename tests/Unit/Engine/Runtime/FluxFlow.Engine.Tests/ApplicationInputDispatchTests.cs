using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationInputDispatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Address =
        ApplicationAddress.WorkflowPort("Main", "Processor", "Input");

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Input_dispatch_rejects_nonpositive_capacity(int capacity)
    {
        var error = Should.Throw<ArgumentOutOfRangeException>(
            () => CreateInput(capacity, new BufferBlock<ApplicationPortRejection>()));
        error.ParamName.ShouldBe("capacity");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Input_capacity_counts_active_work_and_retains_order(int capacity)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(capacity, reports);
        var target = new ControlledTarget();
        var generation = await AttachAsync(input, target);
        var messages = Messages(capacity);
        try
        {
            input.TrySend(messages[0], null).Status.ShouldBe(PortSendStatus.Accepted);
            await target.Entered.Task.WaitAsync(Deadline);
            foreach (var message in messages.Skip(1))
                input.TrySend(message, null).Status.ShouldBe(PortSendStatus.Accepted);

            input.GetStatus().PendingMessages.ShouldBe(capacity);
            input.TrySend(Messages(1)[0], null).Status.ShouldBe(PortSendStatus.Full);
            target.Attempts.ShouldBe(1);
            target.Accepted.ShouldBeEmpty();
            var full = await reports.ReceiveAsync().WaitAsync(Deadline);
            full.Reason.ShouldBe(ApplicationPortRejectionReason.Full);

            var drain = input.DrainAsync(generation, CancellationToken.None).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            input.TrySend(Messages(1)[0], null).Status.ShouldBe(PortSendStatus.Completed);
            target.Release.TrySetResult(true);
            await drain.WaitAsync(Deadline);
            AssertDelivered(target, messages);
            input.GetStatus().PendingMessages.ShouldBe(0);
            input.Complete();
            await input.Completion.WaitAsync(Deadline);
            target.CompleteCalls.ShouldBe(1);
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_work_precedes_queued_work_after_replacement(bool throws)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(3, reports);
        var failure = throws ? new InvalidOperationException("target dispatch failed") : null;
        var retired = new ControlledTarget(failure);
        var replacement = new ControlledTarget();
        var retiredGeneration = await AttachAsync(input, retired);
        var messages = Messages(3);
        try
        {
            foreach (var message in messages)
                input.TrySend(message, null).IsAccepted.ShouldBeTrue();
            await retired.Entered.Task.WaitAsync(Deadline);
            retired.Release.TrySetResult(false);
            var rejected = await reports.ReceiveAsync().WaitAsync(Deadline);
            rejected.Reason.ShouldBe(ApplicationPortRejectionReason.TargetRejected);
            rejected.Exception.ShouldBeSameAs(failure);
            rejected.MessageId.ShouldBe(messages[0].MessageId);
            input.GetStatus().PendingMessages.ShouldBe(3);
            input.GetStatus().Availability.ShouldBe(ApplicationPortAvailability.Unavailable);

            var replacementGeneration = await AttachAsync(input, replacement);
            await retired.Exited.Task.WaitAsync(Deadline);
            await input.DetachAsync(retiredGeneration).AsTask().WaitAsync(Deadline);
            replacement.Release.TrySetResult(true);
            await input.DrainAsync(replacementGeneration, CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertDelivered(replacement, messages);
            retired.Attempts.ShouldBe(1);
            retired.Accepted.ShouldBeEmpty();
            reports.TryReceive(out _).ShouldBeFalse();
            input.Complete();
            await input.Completion.WaitAsync(Deadline);
            replacement.CompleteCalls.ShouldBe(1);
            retired.CompleteCalls.ShouldBe(0);
        }
        finally { input.Abort(); retired.Finish(); replacement.Finish(); }
    }

    [Fact]
    public async Task Completion_reports_each_retained_message_when_target_disappears()
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(3, reports);
        var target = new ControlledTarget();
        await AttachAsync(input, target);
        var messages = Messages(3);
        try
        {
            foreach (var message in messages)
                input.TrySend(message, null).IsAccepted.ShouldBeTrue();
            await target.Entered.Task.WaitAsync(Deadline);
            target.Release.TrySetResult(false);
            (await reports.ReceiveAsync().WaitAsync(Deadline))
                .Reason.ShouldBe(ApplicationPortRejectionReason.TargetRejected);

            input.Complete();
            input.Complete();
            await input.Completion.WaitAsync(Deadline);
            foreach (var message in messages)
            {
                var terminal = await reports.ReceiveAsync().WaitAsync(Deadline);
                terminal.Reason.ShouldBe(ApplicationPortRejectionReason.Completed);
                terminal.MessageId.ShouldBe(message.MessageId);
                terminal.TraceId.ShouldBe(message.TraceId);
            }
            reports.TryReceive(out _).ShouldBeFalse();
            input.GetStatus().PendingMessages.ShouldBe(0);
            input.TrySend(messages[0], null).Status.ShouldBe(PortSendStatus.Completed);
            target.Attempts.ShouldBe(1);
            target.Accepted.ShouldBeEmpty();
            target.CompleteCalls.ShouldBe(0);
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Fact]
    public async Task Abort_releases_revision_waiters_and_does_not_dispatch_queued_work()
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(3, reports);
        var target = new ControlledTarget();
        await AttachAsync(input, target);
        var messages = Messages(3);
        Task? revision = null;
        try
        {
            foreach (var message in messages)
                input.TrySend(message, null).IsAccepted.ShouldBeTrue();
            await target.Entered.Task.WaitAsync(Deadline);
            revision = input.BeginRevisionAsync(CancellationToken.None).AsTask();
            revision.IsCompleted.ShouldBeFalse();
            input.Abort();
            await Should.ThrowAsync<ObjectDisposedException>(() => revision.WaitAsync(Deadline));
            await input.Completion.WaitAsync(Deadline);
            await target.Exited.Task.WaitAsync(Deadline);
            input.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            input.GetStatus().PendingMessages.ShouldBe(0);
            input.TrySend(messages[0], null).Status.ShouldBe(PortSendStatus.Completed);
            target.Attempts.ShouldBe(1);
            target.Accepted.ShouldBeEmpty();
            target.CompleteCalls.ShouldBe(0);
        }
        finally
        {
            input.Abort();
            target.Finish();
            if (revision is not null) await Record.ExceptionAsync(() => revision.WaitAsync(Deadline));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_waits_for_target_shutdown_and_preserves_failure(bool fails)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(3, reports);
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new ControlledTarget(completionGate: shutdown.Task);
        var generation = await AttachAsync(input, target);
        var messages = Messages(3);
        var failure = new InvalidOperationException("target shutdown failed");
        try
        {
            foreach (var message in messages)
                input.TrySend(message, null).IsAccepted.ShouldBeTrue();
            target.Release.TrySetResult(true);
            await input.DrainAsync(generation, CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertDelivered(target, messages);
            input.Complete();
            await target.Completing.Task.WaitAsync(Deadline);
            input.Completion.IsCompleted.ShouldBeFalse();
            input.GetStatus().PendingMessages.ShouldBe(0);
            input.TrySend(messages[0], null).Status.ShouldBe(PortSendStatus.Completed);
            if (fails) shutdown.TrySetException(failure);
            else shutdown.TrySetResult();

            var observed = await Record.ExceptionAsync(() => input.Completion.WaitAsync(Deadline));
            observed.ShouldBeSameAs(fails ? failure : null);
            input.Completion.IsFaulted.ShouldBe(fails);
            input.Completion.IsCompletedSuccessfully.ShouldBe(!fails);
            target.CompleteCalls.ShouldBe(1);
        }
        finally { shutdown.TrySetResult(); input.Abort(); target.Finish(); }
    }

    private static ApplicationInputPortCore CreateInput(
        int capacity, BufferBlock<ApplicationPortRejection> reports)
        => new(Address, ApplicationPortKind.Message, typeof(FlowValue), capacity,
            "Input port", "a native target", report => reports.Post(report), _ => { });

    private static async Task<long> AttachAsync(
        ApplicationInputPortCore input, ControlledTarget target)
    {
        await input.BeginRevisionAsync(CancellationToken.None);
        try { return input.CommitRevision(target); }
        finally { input.EndRevision(); }
    }

    private static FlowMessage[] Messages(int count)
        => Enumerable.Range(0, count).Select(index => FlowMessage.Create(FlowValue.From(index))).ToArray();

    private static void AssertDelivered(ControlledTarget target, FlowMessage[] expected)
    {
        var delivered = target.Accepted.ToArray();
        delivered.Length.ShouldBe(expected.Length);
        for (var index = 0; index < expected.Length; index++)
            delivered[index].ShouldBeSameAs(expected[index]);
        target.Attempts.ShouldBe(expected.Length);
    }

    private sealed class ControlledTarget(Exception? failure = null, Task? completionGate = null) : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<FlowMessage> Accepted { get; } = new();
        public Task Completion => _completion.Task;
        public int Attempts { get; private set; }
        public int CompleteCalls { get; private set; }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage message,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            if (!header.IsValid) throw new ArgumentException("Invalid header.", nameof(header));
            if (consumeToAccept && source is null) throw new ArgumentException("A source is required.", nameof(source));
            if (Completion.IsCompleted) return DataflowMessageStatus.DecliningPermanently;
            // A non-postponable probe is not an attempted asynchronous delivery.
            if (!Release.Task.IsCompleted && source is null && failure is null)
                return DataflowMessageStatus.Declined;
            Attempts++;
            Entered.TrySetResult();
            if (failure is not null) { Exited.TrySetResult(); throw failure; }
            if (!Release.Task.IsCompleted)
            {
                if (source is null) return DataflowMessageStatus.Declined;
                _ = SettlePostponedAsync(header, message, source);
                return DataflowMessageStatus.Postponed;
            }
            try
            {
                if (!Release.Task.Result) return DataflowMessageStatus.DecliningPermanently;
                if (consumeToAccept)
                {
                    message = source!.ConsumeMessage(header, this, out var consumed)!;
                    if (!consumed) return DataflowMessageStatus.NotAvailable;
                }
                Accepted.Enqueue(message);
                return DataflowMessageStatus.Accepted;
            }
            finally { Exited.TrySetResult(); }
        }

        private async Task SettlePostponedAsync(DataflowMessageHeader header, FlowMessage message,
            ISourceBlock<FlowMessage> source)
        {
            try
            {
                await Task.WhenAny(Release.Task, source.Completion);
                if (source.Completion.IsCompleted) return;
                if (!await Release.Task)
                {
                    if (source.ReserveMessage(header, this))
                    {
                        _completion.TrySetResult();
                        source.ReleaseReservation(header, this);
                    }
                    return;
                }
                if (!source.ReserveMessage(header, this)) return;
                // The reservation excludes cancellation while recording this accepted delivery.
                Accepted.Enqueue(message);
                source.ConsumeMessage(header, this, out var consumed);
                if (!consumed) throw new InvalidOperationException("A reserved message was not consumable.");
            }
            catch (Exception error) { _completion.TrySetException(error); }
            finally { Exited.TrySetResult(); }
        }

        public void Complete() => _ = CompleteAsync();
        public void Fault(Exception error) => _completion.TrySetException(error);

        public async Task CompleteAsync()
        {
            CompleteCalls++;
            Completing.TrySetResult();
            try
            {
                if (completionGate is not null) await completionGate;
                _completion.TrySetResult();
                await Completion;
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Finish() { Release.TrySetResult(false); _completion.TrySetResult(); }
    }
}
