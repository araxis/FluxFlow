using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputDeliveryContractTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Lease_request_preserves_exact_values_and_rejects_invalid_ownership()
    {
        var until = Now.AddSeconds(30);
        var request = new DurableOutputDeliveryLeaseRequest("worker-1", Now, until);

        request.OwnerId.ShouldBe("worker-1");
        request.Now.ShouldBe(Now);
        request.Now.Offset.ShouldBe(Now.Offset);
        request.LeaseUntil.ShouldBe(until);
        request.LeaseUntil.Offset.ShouldBe(until.Offset);
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputDeliveryLeaseRequest(null!, Now, until)).ParamName.ShouldBe("ownerId");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryLeaseRequest(" ", Now, until)).ParamName.ShouldBe("ownerId");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryLeaseRequest(" worker-1", Now, until)).ParamName.ShouldBe("ownerId");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryLeaseRequest("worker-1", Now, Now)).ParamName.ShouldBe("leaseUntil");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryLeaseRequest("worker-1", Now, Now.AddTicks(-1)))
            .ParamName.ShouldBe("leaseUntil");
    }

    [Fact]
    public void Lease_preserves_exact_envelope_token_owner_times_and_attempt()
    {
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("4c018d95-5db0-466d-807a-81b69720d358");
        var until = Now.AddMinutes(1);

        var lease = new DurableOutputDeliveryLease(
            envelope,
            token,
            "worker-1",
            Now,
            until,
            attempt: 3);

        lease.Envelope.ShouldBeSameAs(envelope);
        lease.LeaseToken.ShouldBe(token);
        lease.OwnerId.ShouldBe("worker-1");
        lease.LeasedAt.ShouldBe(Now);
        lease.LeaseUntil.ShouldBe(until);
        lease.Attempt.ShouldBe(3);
    }

    [Fact]
    public void Lease_rejects_incomplete_or_inconsistent_values()
    {
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.NewGuid();
        var until = Now.AddSeconds(30);

        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputDeliveryLease(null!, token, "worker", Now, until, 1))
            .ParamName.ShouldBe("envelope");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryLease(envelope, Guid.Empty, "worker", Now, until, 1))
            .ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryLease(envelope, token, "worker ", Now, until, 1))
            .ParamName.ShouldBe("ownerId");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryLease(envelope, token, "worker", Now, Now, 1))
            .ParamName.ShouldBe("leaseUntil");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryLease(envelope, token, "worker", Now, until, 0))
            .ParamName.ShouldBe("attempt");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryLease(envelope, token, "worker", Now, until, -1))
            .ParamName.ShouldBe("attempt");
    }

    [Fact]
    public void Complete_and_retry_preserve_exact_compare_and_set_values()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var token = Guid.Parse("7e37a880-fcf4-4fbf-972f-c5f5df4a0127");
        var next = Now.AddSeconds(5);

        var completion = new DurableOutputDeliveryTransition(key, token, Now);
        var retry = new DurableOutputDeliveryRetry(key, token, Now, next);

        completion.Key.ShouldBe(key);
        completion.LeaseToken.ShouldBe(token);
        completion.OccurredAt.ShouldBe(Now);
        retry.Key.ShouldBe(key);
        retry.LeaseToken.ShouldBe(token);
        retry.ReleasedAt.ShouldBe(Now);
        retry.NextAttemptAt.ShouldBe(next);
        new DurableOutputDeliveryRetry(key, token, Now, Now).NextAttemptAt.ShouldBe(Now);
    }

    [Fact]
    public void Dead_letter_transition_preserves_exact_values_and_rejects_invalid_identity_reason()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var token = Guid.Parse("e9a44975-6126-4f98-b5c9-d744f39e8d47");

        var transition = new DurableOutputDeliveryDeadLetter(
            key,
            token,
            Now,
            DurableOutputDeadLetterReason.HandlerFailure);

        transition.Key.ShouldBe(key);
        transition.LeaseToken.ShouldBe(token);
        transition.DeadLetteredAt.ShouldBe(Now);
        transition.DeadLetteredAt.Offset.ShouldBe(Now.Offset);
        transition.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        Should.Throw<ArgumentException>(() => new DurableOutputDeliveryDeadLetter(
            default, token, Now, DurableOutputDeadLetterReason.HandlerFailure)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() => new DurableOutputDeliveryDeadLetter(
            key, Guid.Empty, Now, DurableOutputDeadLetterReason.HandlerFailure))
            .ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryDeadLetter(
            key, token, Now, (DurableOutputDeadLetterReason)99)).ParamName.ShouldBe("reason");
    }

    [Fact]
    public void Complete_retry_and_result_reject_default_identity_or_invalid_time_and_status()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var token = Guid.NewGuid();

        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryTransition(default, token, Now)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryTransition(key, Guid.Empty, Now)).ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryRetry(default, token, Now, Now)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryRetry(key, Guid.Empty, Now, Now)).ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryRetry(key, token, Now, Now.AddTicks(-1)))
            .ParamName.ShouldBe("nextAttemptAt");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputDeliveryTransitionResult(default, DurableOutputDeliveryTransitionStatus.Applied))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputDeliveryTransitionResult(
                key,
                (DurableOutputDeliveryTransitionStatus)99)).ParamName.ShouldBe("status");
    }

    [Theory]
    [InlineData(DurableOutputDeliveryTransitionStatus.Applied, true)]
    [InlineData(DurableOutputDeliveryTransitionStatus.LeaseLost, false)]
    [InlineData(DurableOutputDeliveryTransitionStatus.NotFound, false)]
    [InlineData(DurableOutputDeliveryTransitionStatus.InvalidState, false)]
    public void Transition_result_exposes_exact_status_and_application(
        DurableOutputDeliveryTransitionStatus status,
        bool isApplied)
    {
        var key = DurableOutputTestData.Envelope().Key;

        var result = new DurableOutputDeliveryTransitionResult(key, status);

        result.Key.ShouldBe(key);
        result.Status.ShouldBe(status);
        result.IsApplied.ShouldBe(isApplied);
    }

    [Fact]
    public void Lease_renewal_preserves_exact_values_offsets_and_record_equality()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var token = Guid.Parse("6635521c-8aa1-419b-b24b-1584e20215f8");
        var renewedAt = new DateTimeOffset(2026, 8, 2, 11, 23, 45, TimeSpan.FromHours(2));
        var leaseUntil = new DateTimeOffset(2026, 8, 2, 10, 24, 9, TimeSpan.FromHours(1));

        var renewal = new DurableOutputDeliveryLeaseRenewal(key, token, renewedAt, leaseUntil);

        renewal.Key.ShouldBe(key);
        renewal.LeaseToken.ShouldBe(token);
        renewal.RenewedAt.ShouldBe(renewedAt);
        renewal.RenewedAt.Offset.ShouldBe(TimeSpan.FromHours(2));
        renewal.LeaseUntil.ShouldBe(leaseUntil);
        renewal.LeaseUntil.Offset.ShouldBe(TimeSpan.FromHours(1));
        renewal.ShouldBe(new DurableOutputDeliveryLeaseRenewal(key, token, renewedAt, leaseUntil));
        renewal.ShouldNotBe(new DurableOutputDeliveryLeaseRenewal(
            key,
            token,
            renewedAt,
            leaseUntil.AddTicks(1)));
    }

    [Fact]
    public void Lease_renewal_rejects_invalid_key_token_and_nonfuture_expiry()
    {
        var key = DurableOutputTestData.Envelope().Key;
        var token = Guid.Parse("08f07b96-9c15-478b-a93b-7c0978f8aa67");
        var renewedAt = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentException>(() => new DurableOutputDeliveryLeaseRenewal(
            default,
            token,
            renewedAt,
            renewedAt.AddTicks(1))).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() => new DurableOutputDeliveryLeaseRenewal(
            key,
            Guid.Empty,
            renewedAt,
            renewedAt.AddTicks(1))).ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryLeaseRenewal(
            key,
            token,
            renewedAt,
            renewedAt)).ParamName.ShouldBe("leaseUntil");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryLeaseRenewal(
            key,
            token,
            renewedAt,
            renewedAt.AddTicks(-1))).ParamName.ShouldBe("leaseUntil");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableOutputDeliveryLeaseRenewal(
            key,
            token,
            renewedAt,
            renewedAt.ToOffset(TimeSpan.Zero))).ParamName.ShouldBe("leaseUntil");
    }

    [Fact]
    public void Contracts_are_immutable_and_delivery_store_is_a_separate_small_capability()
    {
        var immutableTypes = new[]
        {
            typeof(DurableOutputDeliveryLeaseRequest),
            typeof(DurableOutputDeliveryLease),
            typeof(DurableOutputDeliveryLeaseRenewal),
            typeof(DurableOutputDeliveryTransition),
            typeof(DurableOutputDeliveryRetry),
            typeof(DurableOutputDeliveryDeadLetter),
            typeof(DurableOutputDeliveryTransitionResult),
            typeof(DurableOutputDeliveryOptions)
        };

        foreach (var type in immutableTypes)
        {
            type.GetProperties().ShouldAllBe(
                static property => property.SetMethod == null,
                $"{type.Name} must not expose mutable properties");
        }

        typeof(IDurableOutputDeliveryStore).GetInterfaces()
            .ShouldNotContain(typeof(IDurableOutputStore));
        typeof(IDurableOutputStore).GetInterfaces()
            .ShouldNotContain(typeof(IDurableOutputDeliveryStore));
        typeof(IDurableOutputDeliveryStore).GetMethods().Select(static method => method.Name)
            .ShouldBe(
                ["TryLeaseAsync", "RenewLeaseAsync", "CompleteAsync", "RetryAsync", "DeadLetterAsync"],
                ignoreOrder: true);
        var renewalMethod = typeof(IDurableOutputDeliveryStore).GetMethod("RenewLeaseAsync");
        renewalMethod.ShouldNotBeNull();
        renewalMethod!.ReturnType.ShouldBe(typeof(ValueTask<DurableOutputDeliveryTransitionResult>));
        renewalMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(DurableOutputDeliveryLeaseRenewal), typeof(CancellationToken)]);
        renewalMethod.GetParameters()[1].HasDefaultValue.ShouldBeTrue();
        renewalMethod.GetParameters()[1].DefaultValue.ShouldBeNull();
        typeof(IDurableOutputDeliveryHandler).GetMethods().Select(static method => method.Name)
            .ShouldBe(["DeliverAsync"]);
        var publicDeliverySurface = typeof(IDurableOutputDeliveryStore).Assembly
            .GetExportedTypes()
            .Where(static type => type.Name.Contains("DurableOutputDelivery", StringComparison.Ordinal))
            .Select(static type => type.Name)
            .ToArray();
        foreach (var excluded in new[]
        {
            "Transport", "Destination", "Batch", "Parallel", "Retention", "ExactlyOnce"
        })
        {
            publicDeliverySurface.ShouldNotContain(
                name => name.Contains(excluded, StringComparison.OrdinalIgnoreCase));
        }
    }
}
