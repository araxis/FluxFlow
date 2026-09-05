using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowOutputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Options_default_to_finite_capacity()
    {
        new FlowOutputOptions().Capacity.ShouldBe(128);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_reject_non_positive_capacity(int capacity)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => _ = new FlowOutputOptions { Capacity = capacity });

        exception.ParamName.ShouldBe(nameof(FlowOutputOptions.Capacity));
    }

    [Fact]
    public async Task Publish_with_no_subscribers_completes_without_blocking_or_replay()
    {
        await using var output = new FlowOutput<int>();

        await SendAcceptedAsync(output, 1);

        var target = new BufferBlock<int>();
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 2);
        output.Complete();

        (await ReceiveAsync(target)).ShouldBe(2);
        target.TryReceive(out _).ShouldBeFalse();
        await output.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Committed_items_reach_every_active_subscriber_once_in_order()
    {
        await using var output = new FlowOutput<int>();
        var first = new RecordingTarget<int>();
        var second = new RecordingTarget<int>();
        using var firstLink = output.LinkTo(
            first,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var secondLink = output.LinkTo(
            second,
            new DataflowLinkOptions { PropagateCompletion = true });

        foreach (var value in Enumerable.Range(1, 10))
        {
            await SendAcceptedAsync(output, value);
        }

        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        await first.Completion.WaitAsync(Timeout);
        await second.Completion.WaitAsync(Timeout);

        first.Accepted.ShouldBe(Enumerable.Range(1, 10));
        second.Accepted.ShouldBe(Enumerable.Range(1, 10));
    }

    [Fact]
    public async Task Concurrent_publishers_deliver_every_accepted_item_once_to_each_subscriber()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 4 });
        var first = new RecordingTarget<int>();
        var second = new RecordingTarget<int>();
        using var firstLink = output.LinkTo(
            first,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var secondLink = output.LinkTo(
            second,
            new DataflowLinkOptions { PropagateCompletion = true });
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publishers = Enumerable.Range(0, 4)
            .Select(async publisher =>
            {
                await start.Task.WaitAsync(Timeout);
                for (var index = 0; index < 25; index++)
                {
                    await SendAcceptedAsync(output, (publisher * 25) + index);
                }
            })
            .ToArray();

        start.TrySetResult();
        await Task.WhenAll(publishers).WaitAsync(Timeout);
        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        await first.Completion.WaitAsync(Timeout);
        await second.Completion.WaitAsync(Timeout);

        first.Accepted.Length.ShouldBe(100);
        first.Accepted.OrderBy(static value => value).ShouldBe(Enumerable.Range(0, 100));
        second.Accepted.ShouldBe(first.Accepted);
    }

    [Fact]
    public async Task Subscriber_added_after_acceptance_receives_only_later_items()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 1 });
        var first = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var firstLink = output.LinkTo(
            first,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await SendAcceptedAsync(output, 2);

        var second = new RecordingTarget<int>();
        using var secondLink = output.LinkTo(
            second,
            new DataflowLinkOptions { PropagateCompletion = true });
        await SendAcceptedAsync(output, 3);
        output.Complete();

        var firstValues = await ReceiveAsync(first, 3);
        await output.Completion.WaitAsync(Timeout);
        await first.Completion.WaitAsync(Timeout);
        await second.Completion.WaitAsync(Timeout);

        firstValues.ShouldBe([1, 2, 3]);
        second.Accepted.ShouldBe([3]);
    }

    [Fact]
    public async Task Slow_subscriber_fills_ingress_and_backpressures_later_publication()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await SendAcceptedAsync(output, 2);
        await SendAcceptedAsync(output, 3);

        var fourthSend = output.SendAsync(4).AsTask();
        fourthSend.IsCompleted.ShouldBeFalse();

        var values = new List<int> { await ReceiveAsync(target) };
        (await fourthSend.WaitAsync(Timeout)).ShouldBeTrue();
        values.AddRange(await ReceiveAsync(target, 3));

        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        values.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task Rejecting_active_target_faults_output_and_propagating_links()
    {
        await using var output = new FlowOutput<int>();
        var rejecting = new RejectingTarget<int>();
        var healthy = new RecordingTarget<int>();
        using var rejectingLink = output.LinkTo(
            rejecting,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var healthyLink = output.LinkTo(
            healthy,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 42);

        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await output.Completion.WaitAsync(Timeout));
        var rejectingFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await rejecting.Completion.WaitAsync(Timeout));
        var healthyFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await healthy.Completion.WaitAsync(Timeout));

        outputFailure.Message.ShouldBe("A reliable output target stopped accepting messages.");
        rejectingFailure.ShouldBeSameAs(outputFailure);
        healthyFailure.ShouldBeSameAs(outputFailure);
        (await output.SendAsync(43).AsTask().WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Fact]
    public async Task Faulting_active_target_preserves_failure_on_output_and_propagating_links()
    {
        await using var output = new FlowOutput<int>();
        var failure = new InvalidOperationException("Subscriber failed.");
        var faulting = new ThrowingTarget<int>(failure);
        var healthy = new RecordingTarget<int>();
        using var faultingLink = output.LinkTo(
            faulting,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var healthyLink = output.LinkTo(
            healthy,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 42);

        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await output.Completion.WaitAsync(Timeout));
        var faultingFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await faulting.Completion.WaitAsync(Timeout));
        var healthyFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await healthy.Completion.WaitAsync(Timeout));

        outputFailure.ShouldBeSameAs(failure);
        faultingFailure.ShouldBeSameAs(failure);
        healthyFailure.ShouldBeSameAs(failure);
        (await output.SendAsync(43).AsTask().WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Fact]
    public async Task Complete_rejects_pending_and_future_publications_while_draining_accepted_items()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await SendAcceptedAsync(output, 2);
        await SendAcceptedAsync(output, 3);
        var pendingSend = output.SendAsync(4).AsTask();
        pendingSend.IsCompleted.ShouldBeFalse();

        output.Complete();

        (await pendingSend.WaitAsync(Timeout)).ShouldBeFalse();
        (await output.SendAsync(5).AsTask().WaitAsync(Timeout)).ShouldBeFalse();
        output.Completion.IsCompleted.ShouldBeFalse();

        (await ReceiveAsync(target, 3)).ShouldBe([1, 2, 3]);
        await output.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Fault_rejects_pending_publication_and_preserves_original_exception()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 1 });
        var target = new PostponedTarget<int>();
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await target.Offered.WaitAsync(Timeout);
        await SendAcceptedAsync(output, 2);
        var pendingSend = output.SendAsync(3).AsTask();
        pendingSend.IsCompleted.ShouldBeFalse();
        var failure = new InvalidOperationException("Output failed.");

        output.Fault(failure);

        (await pendingSend.WaitAsync(Timeout)).ShouldBeFalse();
        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await output.Completion.WaitAsync(Timeout));
        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await target.Completion.WaitAsync(Timeout));
        outputFailure.ShouldBeSameAs(failure);
        targetFailure.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Canceled_pending_publication_is_not_delivered_and_output_remains_usable()
    {
        await using var output = new FlowOutput<int>(
            new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await SendAcceptedAsync(output, 2);
        await SendAcceptedAsync(output, 3);
        using var cancellation = new CancellationTokenSource();
        var canceledSend = output.SendAsync(4, cancellation.Token).AsTask();
        canceledSend.IsCompleted.ShouldBeFalse();

        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await canceledSend.WaitAsync(Timeout));
        var values = new List<int> { await ReceiveAsync(target) };
        await SendAcceptedAsync(output, 5);
        values.AddRange(await ReceiveAsync(target, 3));

        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        values.ShouldBe([1, 2, 3, 5]);
    }

    [Fact]
    public async Task Disposing_blocked_link_cancels_only_that_delivery()
    {
        await using var output = new FlowOutput<int>();
        var target = new PostponedTarget<int>();
        var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await target.Offered.WaitAsync(Timeout);

        link.Dispose();

        await SendAcceptedAsync(output, 2);
        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        output.Completion.IsFaulted.ShouldBeFalse();
        target.Completion.IsCompleted.ShouldBeFalse();
        target.Complete();
    }

    [Fact]
    public async Task DisposeAsync_racing_with_blocked_publication_releases_publisher_and_is_idempotent()
    {
        var output = new FlowOutput<int>(new FlowOutputOptions { Capacity = 1 });
        var target = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var link = output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await SendAcceptedAsync(output, 1);
        await SendAcceptedAsync(output, 2);
        await SendAcceptedAsync(output, 3);
        var pendingSend = output.SendAsync(4).AsTask();
        pendingSend.IsCompleted.ShouldBeFalse();

        var firstDispose = output.DisposeAsync().AsTask();
        var secondDispose = output.DisposeAsync().AsTask();

        (await pendingSend.WaitAsync(Timeout)).ShouldBeFalse();
        firstDispose.IsCompleted.ShouldBeFalse();
        var values = await ReceiveAsync(target, 3);
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(Timeout);
        await output.DisposeAsync().AsTask().WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);

        values.ShouldBe([1, 2, 3]);
        output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Late_link_receives_existing_completion_or_fault_when_propagation_is_enabled()
    {
        await using var completedOutput = new FlowOutput<int>();
        completedOutput.Complete();
        await completedOutput.Completion.WaitAsync(Timeout);
        var completedTarget = new RecordingTarget<int>();
        using var completedLink = completedOutput.LinkTo(
            completedTarget,
            new DataflowLinkOptions { PropagateCompletion = true });

        await completedTarget.Completion.WaitAsync(Timeout);
        completedTarget.Accepted.ShouldBeEmpty();

        await using var faultedOutput = new FlowOutput<int>();
        var failure = new InvalidOperationException("Output failed before linking.");
        faultedOutput.Fault(failure);
        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await faultedOutput.Completion.WaitAsync(Timeout));
        var faultedTarget = new RecordingTarget<int>();
        using var faultedLink = faultedOutput.LinkTo(
            faultedTarget,
            new DataflowLinkOptions { PropagateCompletion = true });

        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await faultedTarget.Completion.WaitAsync(Timeout));
        outputFailure.ShouldBeSameAs(failure);
        targetFailure.ShouldBeSameAs(failure);
        faultedTarget.Accepted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Link_options_preserve_max_messages_and_completion_propagation()
    {
        await using var output = new FlowOutput<int>();
        var limited = new RecordingTarget<int>();
        var propagated = new RecordingTarget<int>();
        using var limitedLink = output.LinkTo(
            limited,
            new DataflowLinkOptions
            {
                MaxMessages = 2,
                PropagateCompletion = true
            });
        using var propagatedLink = output.LinkTo(
            propagated,
            new DataflowLinkOptions { PropagateCompletion = true });

        foreach (var value in Enumerable.Range(1, 4))
        {
            await SendAcceptedAsync(output, value);
        }

        output.Complete();
        await output.Completion.WaitAsync(Timeout);
        await propagated.Completion.WaitAsync(Timeout);

        limited.Accepted.ShouldBe([1, 2]);
        limited.Completion.IsCompleted.ShouldBeFalse();
        propagated.Accepted.ShouldBe([1, 2, 3, 4]);
        propagated.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        limited.Complete();
    }

    private static async Task SendAcceptedAsync<T>(FlowOutput<T> output, T value)
    {
        var accepted = await output.SendAsync(value).AsTask().WaitAsync(Timeout);
        accepted.ShouldBeTrue();
    }

    private static async Task<T> ReceiveAsync<T>(BufferBlock<T> target)
        => await target.ReceiveAsync().WaitAsync(Timeout);

    private static async Task<List<T>> ReceiveAsync<T>(BufferBlock<T> target, int count)
    {
        var values = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            values.Add(await ReceiveAsync(target));
        }

        return values;
    }

    private sealed class RecordingTarget<T> : ITargetBlock<T>
    {
        private readonly ConcurrentQueue<T> _accepted = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public T[] Accepted => _accepted.ToArray();

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
        {
            if (Completion.IsCompleted)
            {
                return DataflowMessageStatus.DecliningPermanently;
            }

            if (consumeToAccept)
            {
                if (source is null)
                {
                    return DataflowMessageStatus.Declined;
                }

                var consumedValue = source.ConsumeMessage(
                    messageHeader,
                    this,
                    out var messageConsumed);
                if (!messageConsumed)
                {
                    return DataflowMessageStatus.NotAvailable;
                }

                _accepted.Enqueue(consumedValue!);
                return DataflowMessageStatus.Accepted;
            }

            _accepted.Enqueue(messageValue);
            return DataflowMessageStatus.Accepted;
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class RejectingTarget<T> : ITargetBlock<T>
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => DataflowMessageStatus.DecliningPermanently;

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ThrowingTarget<T>(Exception failure) : ITargetBlock<T>
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => throw failure;

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class PostponedTarget<T> : ITargetBlock<T>
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _offered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Offered => _offered.Task;

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
        {
            if (Completion.IsCompleted)
            {
                return DataflowMessageStatus.DecliningPermanently;
            }

            if (source is null)
            {
                return DataflowMessageStatus.Declined;
            }

            _offered.TrySetResult();
            return DataflowMessageStatus.Postponed;
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }
}
