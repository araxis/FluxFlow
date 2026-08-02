using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Executable provider-neutral conformance specification for leased durable
/// output delivery stores.
/// </summary>
public abstract class DurableOutputDeliveryStoreConformanceTests
{
    protected abstract ValueTask<DurableOutputDeliveryStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Captured_outputs_become_candidates_in_exact_deterministic_order()
    {
        await using var context = await CreateStoreAsync();
        var capturedAt = DurableOutputStoreConformanceData.DeliveryNow.AddHours(-1);
        var envelopes = new[]
        {
            DurableOutputStoreConformanceData.Envelope(
                "z-message",
                DurableOutputStoreConformanceData.SecondaryOutput,
                capturedAt: capturedAt.AddMinutes(-1)),
            DurableOutputStoreConformanceData.Envelope("b-message", capturedAt: capturedAt),
            DurableOutputStoreConformanceData.Envelope(
                "middle-message",
                DurableOutputStoreConformanceData.SecondaryOutput,
                capturedAt: capturedAt),
            DurableOutputStoreConformanceData.Envelope("a-message", capturedAt: capturedAt)
        };
        foreach (var envelope in envelopes)
        {
            (await context.CaptureStore.EnqueueAsync(envelope)).Status
                .ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        }

        var leases = new List<DurableOutputDeliveryLease>();
        for (var index = 0; index < envelopes.Length; index++)
        {
            leases.Add((await context.DeliveryStore.TryLeaseAsync(
                Request(DurableOutputStoreConformanceData.DeliveryNow, $"worker-{index}")))
                .ShouldNotBeNull());
        }

        var expected = envelopes
            .OrderBy(static envelope => envelope.CapturedAt.UtcTicks)
            .ThenBy(static envelope => envelope.Address.Value, StringComparer.Ordinal)
            .ThenBy(static envelope => envelope.MessageId.Value, StringComparer.Ordinal)
            .ToArray();
        leases.Select(static lease => lease.Envelope.Key)
            .ShouldBe(expected.Select(static envelope => envelope.Key));
        leases.Select(static lease => lease.Attempt).ShouldAllBe(static attempt => attempt == 1);
        for (var index = 0; index < expected.Length; index++)
            leases[index].Envelope.HasSameContent(expected[index]).ShouldBeTrue();
        (await context.DeliveryStore.TryLeaseAsync(
            Request(DurableOutputStoreConformanceData.DeliveryNow, "worker-empty")))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Pending_record_is_ineligible_before_and_eligible_at_exact_due_boundary()
    {
        await using var context = await CreateStoreAsync();
        var due = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope(
            "due-boundary",
            capturedAt: due);
        (await context.CaptureStore.EnqueueAsync(envelope)).Status
            .ShouldBe(DurableOutputEnqueueStatus.Enqueued);

        (await context.DeliveryStore.TryLeaseAsync(Request(due.AddTicks(-1), "early")))
            .ShouldBeNull();
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(due, "due")))
            .ShouldNotBeNull();

        lease.Envelope.HasSameContent(envelope).ShouldBeTrue();
        lease.OwnerId.ShouldBe("due");
        lease.Attempt.ShouldBe(1);
        ShouldHaveExactTime(lease.LeasedAt, due);
        ShouldHaveExactTime(lease.LeaseUntil, due.AddSeconds(30));
    }

    [Fact]
    public async Task Expired_lease_recovers_with_new_token_owner_exact_times_and_next_attempt()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("expiry");
        await context.CaptureStore.EnqueueAsync(envelope);
        var first = (await context.DeliveryStore.TryLeaseAsync(Request(now, "first")))
            .ShouldNotBeNull();

        (await context.DeliveryStore.TryLeaseAsync(Request(first.LeaseUntil.AddTicks(-1), "early")))
            .ShouldBeNull();
        var second = (await context.DeliveryStore.TryLeaseAsync(
            Request(first.LeaseUntil, "second"))).ShouldNotBeNull();

        first.Envelope.HasSameContent(envelope).ShouldBeTrue();
        second.Envelope.HasSameContent(envelope).ShouldBeTrue();
        second.LeaseToken.ShouldNotBe(first.LeaseToken);
        second.OwnerId.ShouldBe("second");
        second.Attempt.ShouldBe(2);
        ShouldHaveExactTime(second.LeasedAt, first.LeaseUntil);
        ShouldHaveExactTime(second.LeaseUntil, first.LeaseUntil.AddSeconds(30));
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            first.LeaseToken,
            second.LeasedAt.AddTicks(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            second.LeaseToken,
            second.LeasedAt.AddTicks(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    [Fact]
    public async Task Completed_and_dead_lettered_terminal_records_are_not_eligible()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var completed = DurableOutputStoreConformanceData.Envelope("terminal-completed");
        var deadLettered = DurableOutputStoreConformanceData.ErrorEnvelope("terminal-dead");
        await context.CaptureStore.EnqueueAsync(completed);
        await context.CaptureStore.EnqueueAsync(deadLettered);

        var completedLease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        completedLease.Envelope.Key.ShouldBe(completed.Key);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            completed.Key,
            completedLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        var deadLetterLease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "dead")))
            .ShouldNotBeNull();
        deadLetterLease.Envelope.Key.ShouldBe(deadLettered.Key);
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            deadLettered.Key,
            deadLetterLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        (await context.DeliveryStore.TryLeaseAsync(Request(now.AddDays(1), "terminal-check")))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_lease_attempts_for_one_output_have_one_exact_winner()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("one-winner");
        await context.CaptureStore.EnqueueAsync(envelope);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new[]
        {
            Task.Run(async () =>
            {
                await start.Task;
                return await context.DeliveryStore.TryLeaseAsync(Request(now, "worker-a"));
            }),
            Task.Run(async () =>
            {
                await start.Task;
                return await context.DeliveryStore.TryLeaseAsync(Request(now, "worker-b"));
            })
        };

        start.TrySetResult();
        var results = await Task.WhenAll(operations);

        results.Count(static lease => lease is not null).ShouldBe(1);
        results.Count(static lease => lease is null).ShouldBe(1);
        var winner = results.Single(static lease => lease is not null).ShouldNotBeNull();
        winner.Envelope.HasSameContent(envelope).ShouldBeTrue();
        winner.Attempt.ShouldBe(1);
        winner.LeaseToken.ShouldNotBe(Guid.Empty);
        new[] { "worker-a", "worker-b" }.ShouldContain(winner.OwnerId);
        ShouldHaveExactTime(winner.LeasedAt, now);
    }

    [Fact]
    public async Task Concurrent_leasing_of_many_outputs_has_no_duplicate_or_loss()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelopes = Enumerable.Range(0, 8)
            .Select(index => DurableOutputStoreConformanceData.Envelope($"many-{index:D2}"))
            .ToArray();
        foreach (var envelope in envelopes)
            await context.CaptureStore.EnqueueAsync(envelope);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, envelopes.Length)
            .Select(index => Task.Run(async () =>
            {
                await start.Task;
                return await context.DeliveryStore.TryLeaseAsync(
                    Request(now, $"worker-{index:D2}"));
            }))
            .ToArray();

        start.TrySetResult();
        var leases = (await Task.WhenAll(operations))
            .Select(static lease => lease.ShouldNotBeNull())
            .ToArray();

        leases.Select(static lease => lease.Envelope.Key)
            .ShouldBe(envelopes.Select(static envelope => envelope.Key), ignoreOrder: true);
        leases.Select(static lease => lease.Envelope.Key).Distinct().Count()
            .ShouldBe(envelopes.Length);
        leases.Select(static lease => lease.LeaseToken).Distinct().Count()
            .ShouldBe(envelopes.Length);
        leases.Select(static lease => lease.OwnerId).Distinct().Count()
            .ShouldBe(envelopes.Length);
        leases.Select(static lease => lease.Attempt).ShouldAllBe(static attempt => attempt == 1);
        (await context.DeliveryStore.TryLeaseAsync(Request(now, "none-left"))).ShouldBeNull();
    }

    [Fact]
    public async Task Completion_applies_once_with_exact_timestamp_and_permanent_ineligibility()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("complete-once");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now))).ShouldNotBeNull();
        var transition = new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            now.AddSeconds(1));

        var applied = await context.DeliveryStore.CompleteAsync(transition);
        var repeated = await context.DeliveryStore.CompleteAsync(transition);

        applied.ShouldBe(new DurableOutputDeliveryTransitionResult(
            envelope.Key,
            DurableOutputDeliveryTransitionStatus.Applied));
        repeated.ShouldBe(new DurableOutputDeliveryTransitionResult(
            envelope.Key,
            DurableOutputDeliveryTransitionStatus.InvalidState));
        (await context.DeliveryStore.TryLeaseAsync(Request(now.AddDays(1), "after-complete")))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Completion_wrong_key_token_and_expiry_return_exact_status_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("complete-noncurrent");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now))).ShouldNotBeNull();
        var missing = DurableOutputStoreConformanceData.Envelope("complete-missing").Key;

        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            missing,
            Guid.NewGuid(),
            now.AddSeconds(1)))).ShouldBe(new DurableOutputDeliveryTransitionResult(
                missing,
                DurableOutputDeliveryTransitionStatus.NotFound));
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            Guid.NewGuid(),
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            lease.LeaseUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);

        var recovered = (await context.DeliveryStore.TryLeaseAsync(
            Request(lease.LeaseUntil, "recovered"))).ShouldNotBeNull();
        recovered.Envelope.HasSameContent(envelope).ShouldBeTrue();
        recovered.Attempt.ShouldBe(2);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            recovered.LeaseToken,
            recovered.LeasedAt.AddTicks(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    [Fact]
    public async Task Completion_pending_completed_and_dead_lettered_return_invalid_state()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var completed = DurableOutputStoreConformanceData.Envelope("completion-states");
        await context.CaptureStore.EnqueueAsync(completed);
        (await context.DeliveryStore.TryLeaseAsync(Request(
            completed.CapturedAt.AddTicks(-1),
            "initialize-pending"))).ShouldBeNull();

        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            completed.Key,
            Guid.NewGuid(),
            now))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
        var completedLease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        var completion = new DurableOutputDeliveryTransition(
            completed.Key,
            completedLease.LeaseToken,
            now.AddSeconds(1));
        (await context.DeliveryStore.CompleteAsync(completion)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.CompleteAsync(completion)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var dead = DurableOutputStoreConformanceData.Envelope("completion-dead-state");
        await context.CaptureStore.EnqueueAsync(dead);
        var deadLease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "dead-state")))
            .ShouldNotBeNull();
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            dead.Key,
            deadLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            dead.Key,
            deadLease.LeaseToken,
            now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
    }

    [Fact]
    public async Task Retry_reschedules_at_exact_due_boundary_preserves_envelope_and_attempt()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("retry-fidelity");
        await context.CaptureStore.EnqueueAsync(envelope);
        var first = (await context.DeliveryStore.TryLeaseAsync(Request(now))).ShouldNotBeNull();
        var releasedAt = now.AddSeconds(1);
        var due = now.AddMinutes(1);
        var retry = new DurableOutputDeliveryRetry(
            envelope.Key,
            first.LeaseToken,
            releasedAt,
            due);

        (await context.DeliveryStore.RetryAsync(retry)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.RetryAsync(retry)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
        (await context.DeliveryStore.TryLeaseAsync(Request(due.AddTicks(-1), "early")))
            .ShouldBeNull();
        var second = (await context.DeliveryStore.TryLeaseAsync(Request(due, "due")))
            .ShouldNotBeNull();

        second.Envelope.HasSameContent(envelope).ShouldBeTrue();
        second.LeaseToken.ShouldNotBe(first.LeaseToken);
        second.Attempt.ShouldBe(2);
        ShouldHaveExactTime(second.LeasedAt, due);
    }

    [Fact]
    public async Task Retry_wrong_key_token_expiry_and_terminal_states_return_exact_status_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("retry-noncurrent");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now))).ShouldNotBeNull();
        var missing = DurableOutputStoreConformanceData.Envelope("retry-missing").Key;

        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            missing,
            Guid.NewGuid(),
            now.AddSeconds(1),
            now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.NotFound);
        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            envelope.Key,
            Guid.NewGuid(),
            now.AddSeconds(1),
            now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            envelope.Key,
            lease.LeaseToken,
            lease.LeaseUntil,
            lease.LeaseUntil))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        var recovered = (await context.DeliveryStore.TryLeaseAsync(
            Request(lease.LeaseUntil, "retry-recovered"))).ShouldNotBeNull();
        recovered.Attempt.ShouldBe(2);

        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            recovered.LeaseToken,
            recovered.LeasedAt.AddTicks(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            envelope.Key,
            recovered.LeaseToken,
            recovered.LeasedAt.AddTicks(2),
            recovered.LeasedAt.AddTicks(2)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var dead = DurableOutputStoreConformanceData.Envelope("retry-dead-state");
        await context.CaptureStore.EnqueueAsync(dead);
        var deadLease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "retry-dead")))
            .ShouldNotBeNull();
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            dead.Key,
            deadLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            dead.Key,
            deadLease.LeaseToken,
            now.AddSeconds(2),
            now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);

        var pending = DurableOutputStoreConformanceData.Envelope("retry-pending-state");
        await context.CaptureStore.EnqueueAsync(pending);
        (await context.DeliveryStore.TryLeaseAsync(Request(
            pending.CapturedAt.AddTicks(-1),
            "initialize-retry-pending"))).ShouldBeNull();
        (await context.DeliveryStore.RetryAsync(new DurableOutputDeliveryRetry(
            pending.Key,
            Guid.NewGuid(),
            now.AddSeconds(2),
            now.AddSeconds(2)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Renewal_shortens_or_extends_to_the_exact_expiry_boundary(bool extend)
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope(
            extend ? "renew-extend-boundary" : "renew-shorten-boundary");
        await context.CaptureStore.EnqueueAsync(envelope);
        var first = (await context.DeliveryStore.TryLeaseAsync(Request(now, "original-owner")))
            .ShouldNotBeNull();
        var renewedAt = now.AddSeconds(1);
        var renewedUntil = extend ? now.AddMinutes(2) : now.AddSeconds(5);

        var result = await context.DeliveryStore.RenewLeaseAsync(
            Renewal(first, renewedAt, renewedUntil));

        result.ShouldBe(new DurableOutputDeliveryTransitionResult(
            envelope.Key,
            DurableOutputDeliveryTransitionStatus.Applied));
        (await context.DeliveryStore.TryLeaseAsync(
            Request(renewedUntil.AddTicks(-1), "too-early"))).ShouldBeNull();
        var second = (await context.DeliveryStore.TryLeaseAsync(
            Request(renewedUntil, "new-owner"))).ShouldNotBeNull();
        second.Envelope.HasSameContent(envelope).ShouldBeTrue();
        second.LeaseToken.ShouldNotBe(first.LeaseToken);
        second.OwnerId.ShouldBe("new-owner");
        second.Attempt.ShouldBe(2);
        ShouldHaveExactTime(second.LeasedAt, renewedUntil);
        ShouldHaveExactTime(second.LeaseUntil, renewedUntil.AddSeconds(30));
    }

    [Fact]
    public async Task Successful_renewal_keeps_same_token_settleable_and_prevents_early_reclaim()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.ErrorEnvelope("renew-settleable");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "same-owner")))
            .ShouldNotBeNull();
        var renewedUntil = now.AddMinutes(2);

        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(lease, now.AddSeconds(1), renewedUntil))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.TryLeaseAsync(
            Request(lease.LeaseUntil, "competing-owner"))).ShouldBeNull();
        var completion = await context.DeliveryStore.CompleteAsync(
            new DurableOutputDeliveryTransition(
                envelope.Key,
                lease.LeaseToken,
                lease.LeaseUntil.AddSeconds(1)));
        completion.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.TryLeaseAsync(
            Request(renewedUntil.AddDays(1), "after-completion"))).ShouldBeNull();
    }

    [Fact]
    public async Task Wrong_token_renewal_is_lease_lost_without_mutation()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("renew-wrong-token");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "current-owner")))
            .ShouldNotBeNull();

        var result = await context.DeliveryStore.RenewLeaseAsync(
            new DurableOutputDeliveryLeaseRenewal(
                envelope.Key,
                Guid.Parse("89045a0e-9721-4fdc-b858-f32d23bbb16a"),
                now.AddSeconds(1),
                now.AddMinutes(5)));

        result.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.TryLeaseAsync(
            Request(lease.LeaseUntil.AddTicks(-1), "too-early"))).ShouldBeNull();
        var recovered = (await context.DeliveryStore.TryLeaseAsync(
            Request(lease.LeaseUntil, "recovered"))).ShouldNotBeNull();
        recovered.LeaseToken.ShouldNotBe(lease.LeaseToken);
        recovered.Attempt.ShouldBe(2);
        recovered.OwnerId.ShouldBe("recovered");
    }

    [Fact]
    public async Task Renewal_at_expiry_is_lost_while_one_tick_before_is_applied()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var expiredEnvelope = DurableOutputStoreConformanceData.Envelope("renew-at-expiry");
        var currentEnvelope = DurableOutputStoreConformanceData.Envelope("renew-before-expiry");
        await context.CaptureStore.EnqueueAsync(expiredEnvelope);
        await context.CaptureStore.EnqueueAsync(currentEnvelope);
        var expired = (await context.DeliveryStore.TryLeaseAsync(Request(now, "expired-owner")))
            .ShouldNotBeNull();
        var current = (await context.DeliveryStore.TryLeaseAsync(Request(now, "current-owner")))
            .ShouldNotBeNull();
        var extendedUntil = current.LeaseUntil.AddMinutes(1);

        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(expired, expired.LeaseUntil, expired.LeaseUntil.AddMinutes(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.LeaseLost);
        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(current, current.LeaseUntil.AddTicks(-1), extendedUntil))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        var recovered = (await context.DeliveryStore.TryLeaseAsync(
            Request(expired.LeaseUntil, "expiry-winner"))).ShouldNotBeNull();
        recovered.Envelope.Key.ShouldBe(expiredEnvelope.Key);
        recovered.Attempt.ShouldBe(2);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            recovered.Envelope.Key,
            recovered.LeaseToken,
            recovered.LeasedAt.AddTicks(1)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.TryLeaseAsync(
            Request(current.LeaseUntil, "blocked-by-renewal"))).ShouldBeNull();
        var renewedRecovery = (await context.DeliveryStore.TryLeaseAsync(
            Request(extendedUntil, "renewed-recovery"))).ShouldNotBeNull();
        renewedRecovery.Envelope.Key.ShouldBe(currentEnvelope.Key);
        renewedRecovery.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task Missing_and_nonleased_states_return_exact_renewal_status()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var pending = DurableOutputStoreConformanceData.Envelope("renew-pending");
        var completed = DurableOutputStoreConformanceData.Envelope("renew-completed");
        var dead = DurableOutputStoreConformanceData.Envelope("renew-dead");
        await context.CaptureStore.EnqueueAsync(pending);
        await context.CaptureStore.EnqueueAsync(completed);
        await context.CaptureStore.EnqueueAsync(dead);
        (await context.DeliveryStore.TryLeaseAsync(
            Request(pending.CapturedAt.AddTicks(-1), "initialize-pending"))).ShouldBeNull();
        var completedLease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "complete")))
            .ShouldNotBeNull();
        completedLease.Envelope.Key.ShouldBe(completed.Key);
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            completed.Key,
            completedLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        var deadLease = (await context.DeliveryStore.TryLeaseAsync(Request(now, "dead")))
            .ShouldNotBeNull();
        deadLease.Envelope.Key.ShouldBe(dead.Key);
        (await context.DeliveryStore.DeadLetterAsync(DeadLetter(
            dead.Key,
            deadLease.LeaseToken,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        var missing = DurableOutputStoreConformanceData.Envelope("renew-missing").Key;

        (await context.DeliveryStore.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
            missing,
            Guid.NewGuid(),
            now,
            now.AddSeconds(1)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.NotFound);
        foreach (var key in new[] { pending.Key, completed.Key, dead.Key })
        {
            (await context.DeliveryStore.RenewLeaseAsync(new DurableOutputDeliveryLeaseRenewal(
                key,
                Guid.NewGuid(),
                now.AddSeconds(2),
                now.AddSeconds(3)))).Status
                .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
        }
    }

    [Fact]
    public async Task Repeated_renewal_uses_the_current_persisted_expiry()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("renew-repeated");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        var firstUntil = now.AddMinutes(2);
        var secondRenewedAt = lease.LeaseUntil.AddSeconds(1);
        var secondUntil = now.AddMinutes(3);

        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(lease, now.AddSeconds(1), firstUntil))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(lease, secondRenewedAt, secondUntil))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);

        (await context.DeliveryStore.TryLeaseAsync(
            Request(firstUntil, "old-renewal-expiry"))).ShouldBeNull();
        var recovered = (await context.DeliveryStore.TryLeaseAsync(
            Request(secondUntil, "latest-expiry"))).ShouldNotBeNull();
        recovered.Attempt.ShouldBe(2);
        recovered.OwnerId.ShouldBe("latest-expiry");
    }

    [Fact]
    public async Task Precancelled_renewal_does_not_mutate_the_current_lease()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("renew-canceled");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.RenewLeaseAsync(
                Renewal(lease, now.AddSeconds(1), now.AddSeconds(2)),
                canceled.Token).AsTask());

        (await context.DeliveryStore.TryLeaseAsync(
            Request(now.AddSeconds(2), "would-win-if-shortened"))).ShouldBeNull();
        (await context.DeliveryStore.CompleteAsync(new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            now.AddSeconds(3)))).Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
    }

    [Theory]
    [InlineData(RenewalSettlement.Complete)]
    [InlineData(RenewalSettlement.Retry)]
    [InlineData(RenewalSettlement.DeadLetter)]
    public async Task Renewal_racing_settlement_has_only_valid_atomic_outcomes(
        RenewalSettlement settlement)
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope($"renew-race-{settlement}");
        await context.CaptureStore.EnqueueAsync(envelope);
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalOperation = Task.Run(async () =>
        {
            await start.Task;
            return await context.DeliveryStore.RenewLeaseAsync(
                Renewal(lease, now.AddSeconds(1), now.AddMinutes(2)));
        });
        var settlementOperation = Task.Run(async () =>
        {
            await start.Task;
            return await SettleAsync();
        });

        start.TrySetResult();
        var renewalResult = await renewalOperation;
        var settlementResult = await settlementOperation;

        settlementResult.Status.ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        new[]
        {
            DurableOutputDeliveryTransitionStatus.Applied,
            DurableOutputDeliveryTransitionStatus.InvalidState
        }.ShouldContain(renewalResult.Status);
        (await context.DeliveryStore.RenewLeaseAsync(
            Renewal(lease, now.AddSeconds(2), now.AddMinutes(3)))).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.InvalidState);
        if (settlement == RenewalSettlement.Retry)
        {
            var retryLease = (await context.DeliveryStore.TryLeaseAsync(
                Request(now.AddSeconds(5), "retry-after-race"))).ShouldNotBeNull();
            retryLease.Envelope.HasSameContent(envelope).ShouldBeTrue();
            retryLease.Attempt.ShouldBe(2);
        }
        else
        {
            (await context.DeliveryStore.TryLeaseAsync(
                Request(now.AddDays(1), "terminal-after-race"))).ShouldBeNull();
        }

        ValueTask<DurableOutputDeliveryTransitionResult> SettleAsync()
            => settlement switch
            {
                RenewalSettlement.Complete => context.DeliveryStore.CompleteAsync(
                    new DurableOutputDeliveryTransition(
                        envelope.Key,
                        lease.LeaseToken,
                        now.AddSeconds(1))),
                RenewalSettlement.Retry => context.DeliveryStore.RetryAsync(
                    new DurableOutputDeliveryRetry(
                        envelope.Key,
                        lease.LeaseToken,
                        now.AddSeconds(1),
                        now.AddSeconds(5))),
                RenewalSettlement.DeadLetter => context.DeliveryStore.DeadLetterAsync(
                    DeadLetter(envelope.Key, lease.LeaseToken, now.AddSeconds(1))),
                _ => throw new ArgumentOutOfRangeException(nameof(settlement), settlement, null)
            };
    }

    [Fact]
    public async Task Precancelled_delivery_operations_do_not_mutate_state()
    {
        await using var context = await CreateStoreAsync();
        var now = DurableOutputStoreConformanceData.DeliveryNow;
        var envelope = DurableOutputStoreConformanceData.Envelope("cancel-delivery");
        await context.CaptureStore.EnqueueAsync(envelope);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.TryLeaseAsync(Request(now), canceled.Token).AsTask());
        var lease = (await context.DeliveryStore.TryLeaseAsync(Request(now)))
            .ShouldNotBeNull();
        lease.Attempt.ShouldBe(1);
        var completion = new DurableOutputDeliveryTransition(
            envelope.Key,
            lease.LeaseToken,
            now.AddSeconds(1));
        var retry = new DurableOutputDeliveryRetry(
            envelope.Key,
            lease.LeaseToken,
            now.AddSeconds(1),
            now.AddSeconds(2));
        var deadLetter = DeadLetter(envelope.Key, lease.LeaseToken, now.AddSeconds(1));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.CompleteAsync(completion, canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.RetryAsync(retry, canceled.Token).AsTask());
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.DeliveryStore.DeadLetterAsync(deadLetter, canceled.Token).AsTask());
        (await context.DeliveryStore.CompleteAsync(completion)).Status
            .ShouldBe(DurableOutputDeliveryTransitionStatus.Applied);
        (await context.DeliveryStore.TryLeaseAsync(Request(now.AddDays(1), "after-cancel")))
            .ShouldBeNull();
    }

    private static DurableOutputDeliveryLeaseRequest Request(
        DateTimeOffset now,
        string ownerId = "worker-1")
        => DurableOutputStoreConformanceData.DeliveryRequest(now, ownerId);

    private static DurableOutputDeliveryDeadLetter DeadLetter(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt)
        => DurableOutputStoreConformanceData.DeadLetter(key, leaseToken, deadLetteredAt);

    private static DurableOutputDeliveryLeaseRenewal Renewal(
        DurableOutputDeliveryLease lease,
        DateTimeOffset renewedAt,
        DateTimeOffset leaseUntil)
        => new(lease.Envelope.Key, lease.LeaseToken, renewedAt, leaseUntil);

    private static void ShouldHaveExactTime(DateTimeOffset actual, DateTimeOffset expected)
    {
        actual.UtcTicks.ShouldBe(expected.UtcTicks);
        actual.Offset.ShouldBe(expected.Offset);
    }

    public enum RenewalSettlement
    {
        Complete,
        Retry,
        DeadLetter
    }
}
