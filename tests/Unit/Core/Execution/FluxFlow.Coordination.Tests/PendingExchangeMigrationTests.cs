using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Coordination.Tests;

public sealed class PendingExchangeMigrationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Fault_all_returns_independent_batches_while_consumers_finish()
    {
        await using var exchanges = new PendingExchangeCoordinator<string, string, int>(
            new PendingExchangeCoordinatorOptions { MaxPending = 1 });
        var firstError = new InvalidOperationException("first batch failed");
        var secondError = new InvalidOperationException("second batch failed");
        var first = exchanges.TryStart("first", "first context");
        first.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PendingExchangeCompletion<string, string, int>> ConsumeFirstAsync()
        {
            var result = await first.Completion!;
            entered.TrySetResult();
            await release.Task;
            return result;
        }
        var consuming = ConsumeFirstAsync();
        var firstBatch = exchanges.FaultAll(firstError);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            exchanges.PendingCount.ShouldBe(0);
            exchanges.IsStopped.ShouldBeFalse();
            var second = exchanges.TryStart("second", "second context");
            second.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
            var secondBatch = exchanges.FaultAll(secondError);
            consuming.IsCompleted.ShouldBeFalse();
            var secondResult = await second.Completion!.WaitAsync(Deadline);
            secondBatch.ShouldHaveSingleItem().ShouldBeSameAs(secondResult);
            secondResult.Key.ShouldBe("second");
            secondResult.Context.ShouldBe("second context");
            secondResult.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
            secondResult.Error.ShouldBeSameAs(secondError);
            secondResult.Outcome.ShouldBe(0);
            var firstResult = firstBatch.ShouldHaveSingleItem();
            firstResult.ShouldBeSameAs(await first.Completion!);
            firstResult.Key.ShouldBe("first");
            firstResult.Context.ShouldBe("first context");
            firstResult.Kind.ShouldBe(PendingExchangeCompletionKind.Faulted);
            firstResult.Error.ShouldBeSameAs(firstError);
            exchanges.FaultAll(secondError).ShouldBeEmpty();
            exchanges.PendingCount.ShouldBe(0);
        }
        finally
        {
            release.TrySetResult();
            await consuming.WaitAsync(Deadline);
        }
        (await consuming).ShouldBeSameAs(firstBatch[0]);
    }

    [Fact]
    public async Task Cancellation_returns_context_and_cannot_later_resolve_as_a_reply()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        await using var exchanges = new PendingExchangeCoordinator<string, string, int>(timeProvider: clock);
        var started = exchanges.TryStart("request", "owned context", TimeSpan.FromSeconds(1));
        var error = new OperationCanceledException(cancellation.Token);

        var feedback = exchanges.TryCancel("request", error);
        var completed = await started.Completion!.WaitAsync(Deadline);

        feedback.Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        feedback.Completion.ShouldBeSameAs(completed);
        completed.Key.ShouldBe("request");
        completed.Context.ShouldBe("owned context");
        completed.Kind.ShouldBe(PendingExchangeCompletionKind.Cancelled);
        completed.Error.ShouldBeSameAs(error);
        completed.Outcome.ShouldBe(0);
        clock.Advance(TimeSpan.FromHours(1));
        exchanges.TryResolve("request", 99).Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        exchanges.TryCancel("request").Status.ShouldBe(PendingExchangeFeedbackStatus.Late);
        (await started.Completion).ShouldBeSameAs(completed);
        var missing = exchanges.TryCancel("missing");
        missing.Status.ShouldBe(PendingExchangeFeedbackStatus.NotFound);
        missing.Completion.ShouldBeNull();
        exchanges.PendingCount.ShouldBe(0);
        exchanges.IsStopped.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "context", "key")]
    [InlineData("key", null, "context")]
    public async Task Missing_key_or_context_is_rejected_without_consuming_capacity(
        string? key, string? context, string parameter)
    {
        await using var exchanges = new PendingExchangeCoordinator<string, string, int>(
            new PendingExchangeCoordinatorOptions { MaxPending = 1 });
        Should.Throw<ArgumentNullException>(() => exchanges.TryStart(key!, context!)).ParamName.ShouldBe(parameter);
        exchanges.PendingCount.ShouldBe(0);
        exchanges.IsStopped.ShouldBeFalse();
        var next = exchanges.TryStart("valid", "next context");
        next.Status.ShouldBe(PendingExchangeStartStatus.Accepted);
        exchanges.TryResolve("valid", 42).Status.ShouldBe(PendingExchangeFeedbackStatus.Resolved);
        (await next.Completion!.WaitAsync(Deadline)).Outcome.ShouldBe(42);
    }
}
