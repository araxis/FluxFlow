using FluxFlow.Engine.DurableInput;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputCompletionTests
{
    [Fact]
    public void Acknowledgement_modes_keep_stable_values_and_engine_acceptance_default()
    {
        ((int)DurableInputAcknowledgementMode.EngineAccepted).ShouldBe(0);
        ((int)DurableInputAcknowledgementMode.WorkflowCompleted).ShouldBe(1);
        ((int)DurableInputFailureKind.CompletionSourceUnavailable).ShouldBe(12);
        ((int)DurableInputFailureKind.WorkflowCompletionFailed).ShouldBe(13);
        ((int)DurableInputFailureKind.WorkflowCompletionTimedOut).ShouldBe(14);
        DurableInputOptions.Default.AcknowledgementMode
            .ShouldBe(DurableInputAcknowledgementMode.EngineAccepted);
        DurableInputOptions.Default.WorkflowCompletionTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        DurableInputOptions.Default.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Existing_options_constructor_preserves_the_lightweight_defaults()
    {
        var options = new DurableInputOptions(
            batchSize: 3,
            leaseDuration: TimeSpan.FromSeconds(20),
            pollInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromSeconds(2),
            storeFailureDelay: TimeSpan.FromSeconds(3),
            maxDeliveryAttempts: 4);

        options.AcknowledgementMode.ShouldBe(DurableInputAcknowledgementMode.EngineAccepted);
        options.WorkflowCompletionTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        options.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Full_options_constructor_accepts_explicit_workflow_completion_and_infinite_timeout()
    {
        var options = WorkflowOptions(Timeout.InfiniteTimeSpan);

        options.AcknowledgementMode.ShouldBe(DurableInputAcknowledgementMode.WorkflowCompleted);
        options.WorkflowCompletionTimeout.ShouldBe(Timeout.InfiniteTimeSpan);
        options.LeaseRenewalInterval.ShouldBe(TimeSpan.FromSeconds(5));
        options.LeaseDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Full_options_reject_undefined_mode_and_invalid_completion_timing()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WorkflowOptions(
                TimeSpan.FromMinutes(1),
                acknowledgementMode: (DurableInputAcknowledgementMode)42))
            .ParamName.ShouldBe("acknowledgementMode");
        Should.Throw<ArgumentOutOfRangeException>(() => WorkflowOptions(TimeSpan.Zero))
            .ParamName.ShouldBe("workflowCompletionTimeout");
        Should.Throw<ArgumentOutOfRangeException>(() => WorkflowOptions(
                TimeSpan.FromMinutes(1),
                leaseRenewalInterval: TimeSpan.Zero))
            .ParamName.ShouldBe("leaseRenewalInterval");
        Should.Throw<ArgumentOutOfRangeException>(() => WorkflowOptions(
                TimeSpan.FromMinutes(1),
                leaseRenewalInterval: TimeSpan.FromSeconds(30)))
            .ParamName.ShouldBe("leaseRenewalInterval");
    }

    [Fact]
    public void Engine_accepted_mode_does_not_couple_renewal_interval_to_lease_duration()
    {
        var options = new DurableInputOptions(
            batchSize: 1,
            leaseDuration: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(1),
            maxDeliveryAttempts: 1,
            DurableInputAcknowledgementMode.EngineAccepted,
            TimeSpan.FromMinutes(1),
            leaseRenewalInterval: TimeSpan.FromMinutes(1));

        options.LeaseRenewalInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.AcknowledgementMode.ShouldBe(DurableInputAcknowledgementMode.EngineAccepted);
    }

    [Fact]
    public void Lease_renewal_owns_exact_identity_and_rejects_ambiguous_time_or_token()
    {
        var envelope = DurableInputTestData.Envelope();
        var token = Guid.NewGuid();
        var renewal = new DurableInputLeaseRenewal(
            envelope.Key,
            token,
            DurableInputTestData.Now,
            DurableInputTestData.Now.AddSeconds(30));

        renewal.Key.ShouldBe(envelope.Key);
        renewal.LeaseToken.ShouldBe(token);
        renewal.RenewedAt.ShouldBe(DurableInputTestData.Now);
        renewal.LeaseUntil.ShouldBe(DurableInputTestData.Now.AddSeconds(30));
        Should.Throw<ArgumentException>(() => new DurableInputLeaseRenewal(
                envelope.Key,
                Guid.Empty,
                DurableInputTestData.Now,
                DurableInputTestData.Now.AddSeconds(30)))
            .ParamName.ShouldBe("leaseToken");
        Should.Throw<ArgumentOutOfRangeException>(() => new DurableInputLeaseRenewal(
                envelope.Key,
                token,
                DurableInputTestData.Now,
                DurableInputTestData.Now))
            .ParamName.ShouldBe("leaseUntil");
    }

    [Fact]
    public void Completion_results_are_explicit_immutable_success_or_failure_values()
    {
        var completed = DurableInputCompletionResult.Completed;
        var failed = DurableInputCompletionResult.Failed("The terminal operation failed.");

        completed.IsCompleted.ShouldBeTrue();
        DurableInputCompletionResult.Completed.ShouldBeSameAs(completed);
        completed.FailureDescription.ShouldBeNull();
        failed.IsCompleted.ShouldBeFalse();
        failed.FailureDescription.ShouldBe("The terminal operation failed.");
        Should.Throw<ArgumentException>(() => DurableInputCompletionResult.Failed(" "))
            .ParamName.ShouldBe("description");
        Should.Throw<ArgumentException>(() => DurableInputCompletionResult.Failed(" padded "))
            .ParamName.ShouldBe("description");
    }

    private static DurableInputOptions WorkflowOptions(
        TimeSpan timeout,
        DurableInputAcknowledgementMode acknowledgementMode =
            DurableInputAcknowledgementMode.WorkflowCompleted,
        TimeSpan? leaseRenewalInterval = null)
        => new(
            batchSize: 64,
            leaseDuration: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(2),
            maxDeliveryAttempts: 10,
            acknowledgementMode,
            timeout,
            leaseRenewalInterval ?? TimeSpan.FromSeconds(5));
}
