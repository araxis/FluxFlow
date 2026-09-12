using System.Threading.Tasks.Dataflow;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationInputDispatchTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Input_async_admission_waits_for_capacity_and_drain_includes_started_sends(int capacity)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(capacity, reports);
        var target = new ControlledTarget();
        var generation = await AttachAsync(input, target);
        var messages = Messages(capacity + 1);
        try
        {
            foreach (var message in messages.Take(capacity))
                (await input.SendAsync(message, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            await target.Entered.Task.WaitAsync(Deadline);
            var admission = input.SendAsync(messages[^1], null, CancellationToken.None).AsTask();
            admission.IsCompleted.ShouldBeFalse();
            input.GetStatus().PendingMessages.ShouldBe(capacity);
            reports.TryReceive(out _).ShouldBeFalse();
            var drain = input.DrainAsync(generation, CancellationToken.None).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            (await input.SendAsync(Messages(1)[0], null, CancellationToken.None)).Status.ShouldBe(PortSendStatus.Completed);
            input.TrySend(Messages(1)[0], null).Status.ShouldBe(PortSendStatus.Completed);
            target.Attempts.ShouldBe(1);
            target.Release.TrySetResult(true);
            (await admission.WaitAsync(Deadline)).IsAccepted.ShouldBeTrue();
            await drain.WaitAsync(Deadline);
            AssertDelivered(target, messages);
            input.GetStatus().PendingMessages.ShouldBe(0);

            var replacement = new ControlledTarget();
            replacement.Release.TrySetResult(true);
            var nextGeneration = await AttachAsync(input, replacement);
            (await input.SendAsync(messages[0], null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            await input.DrainAsync(nextGeneration, CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertDelivered(replacement, [messages[0]]);
            input.Complete();
            await input.Completion.WaitAsync(Deadline);
            replacement.CompleteCalls.ShouldBe(1);
            target.CompleteCalls.ShouldBe(0);
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Fact]
    public async Task Input_async_admission_cancellation_preserves_capacity_and_remaining_order()
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(2, reports);
        var target = new ControlledTarget();
        var generation = await AttachAsync(input, target);
        var messages = Messages(4);
        using var cancellation = new CancellationTokenSource();
        try
        {
            input.TrySend(messages[0], null).IsAccepted.ShouldBeTrue();
            input.TrySend(messages[1], null).IsAccepted.ShouldBeTrue();
            await target.Entered.Task.WaitAsync(Deadline);
            var canceled = input.SendAsync(messages[2], null, cancellation.Token).AsTask();
            var remaining = input.SendAsync(messages[3], null, CancellationToken.None).AsTask();
            canceled.IsCompleted.ShouldBeFalse();
            remaining.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            (await Should.ThrowAsync<OperationCanceledException>(() => canceled.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
            input.GetStatus().PendingMessages.ShouldBe(2);
            reports.TryReceive(out _).ShouldBeFalse();
            remaining.IsCompleted.ShouldBeFalse();
            target.Release.TrySetResult(true);
            (await remaining.WaitAsync(Deadline)).IsAccepted.ShouldBeTrue();
            await input.DrainAsync(generation, CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertDelivered(target, [messages[0], messages[1], messages[3]]);
            input.GetStatus().PendingMessages.ShouldBe(0);
            reports.TryReceive(out _).ShouldBeFalse();
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_async_admission_settles_on_complete_or_abort(bool abort)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(2, reports);
        var target = new ControlledTarget();
        await AttachAsync(input, target);
        var messages = Messages(3);
        try
        {
            input.TrySend(messages[0], null).IsAccepted.ShouldBeTrue();
            input.TrySend(messages[1], null).IsAccepted.ShouldBeTrue();
            await target.Entered.Task.WaitAsync(Deadline);
            var waiting = input.SendAsync(messages[2], null, CancellationToken.None).AsTask();
            waiting.IsCompleted.ShouldBeFalse();
            if (abort) input.Abort();
            else input.Complete();
            (await waiting.WaitAsync(Deadline)).Status.ShouldBe(PortSendStatus.Completed);
            var rejection = await reports.ReceiveAsync().WaitAsync(Deadline);
            rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Completed);
            rejection.MessageId.ShouldBe(messages[2].MessageId);
            (await input.SendAsync(messages[2], null, CancellationToken.None)).Status.ShouldBe(PortSendStatus.Completed);
            target.Release.TrySetResult(true);
            await input.Completion.WaitAsync(Deadline);
            if (abort)
            {
                await target.Exited.Task.WaitAsync(Deadline);
                target.Attempts.ShouldBe(1);
                target.Accepted.ShouldBeEmpty();
            }
            else AssertDelivered(target, messages[..2]);
            input.GetStatus().PendingMessages.ShouldBe(0);
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Input_async_admission_fast_dispatch_does_not_strand_drain(int capacity)
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(capacity, reports);
        var target = new ControlledTarget();
        target.Release.TrySetResult(true);
        var generation = await AttachAsync(input, target);
        var messages = Messages(256);
        try
        {
            var admissions = messages.Select(message => input.SendAsync(message, null, CancellationToken.None).AsTask()).ToArray();
            var results = await Task.WhenAll(admissions).WaitAsync(Deadline);
            results.All(result => result.IsAccepted).ShouldBeTrue();
            var drain = input.DrainAsync(generation, CancellationToken.None).AsTask();
            await drain.WaitAsync(Deadline);
            AssertDelivered(target, messages);
            reports.TryReceive(out _).ShouldBeFalse();
            input.GetStatus().PendingMessages.ShouldBe(0);
            input.Complete();
            await input.Completion.WaitAsync(Deadline);
            input.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            target.CompleteCalls.ShouldBe(1);
        }
        finally { input.Abort(); target.Finish(); }
    }

    [Fact]
    public async Task Input_async_admission_crosses_replacement_without_replaying_claimed_work()
    {
        var reports = new BufferBlock<ApplicationPortRejection>();
        var input = CreateInput(2, reports);
        var previous = new ControlledTarget();
        var replacement = new ControlledTarget();
        await AttachAsync(input, previous);
        var messages = Messages(3);
        try
        {
            (await input.SendAsync(messages[0], null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            await previous.Entered.Task.WaitAsync(Deadline);
            (await input.SendAsync(messages[1], null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            var waiting = input.SendAsync(messages[2], null, CancellationToken.None).AsTask();
            waiting.IsCompleted.ShouldBeFalse();
            var revision = input.BeginRevisionAsync(CancellationToken.None).AsTask();
            revision.IsCompleted.ShouldBeFalse();
            previous.Release.TrySetResult(true);
            await revision.WaitAsync(Deadline);
            long generation;
            try { generation = input.CommitRevision(replacement); }
            finally { input.EndRevision(); }
            (await waiting.WaitAsync(Deadline)).IsAccepted.ShouldBeTrue();
            await replacement.Entered.Task.WaitAsync(Deadline);
            AssertDelivered(previous, [messages[0]]);
            replacement.Accepted.ShouldBeEmpty();
            replacement.Release.TrySetResult(true);
            await input.DrainAsync(generation, CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertDelivered(replacement, messages[1..]);
            input.GetStatus().PendingMessages.ShouldBe(0);
            reports.TryReceive(out _).ShouldBeFalse();
        }
        finally { input.Abort(); previous.Finish(); replacement.Finish(); }
    }
}
