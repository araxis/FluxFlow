using FluxFlow.Data;
using FluxFlow.Engine.Benchmarks.LoadQualification;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

public sealed class LoadMeasurementsTests
{
    [Fact]
    public void Declined_admission_cannot_hide_a_partial_delivery()
    {
        var measurements = new LoadMeasurements(2);
        var message = Message();
        measurements.Register(message);
        measurements.Accept(.25);
        measurements.Receive(0, message);
        Should.Throw<InvalidOperationException>(() => measurements.Decline(message, canceled: true));
        measurements.Canceled.ShouldBe(0);
        measurements.Pending.ShouldBe(1);
        measurements.TargetCounts.ShouldBe(new long[] { 1, 0 });
        measurements.Receive(1, message);
        measurements.ValidateDrained();
        measurements.Completed.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void Fast_fanout_can_finish_before_admission_returns(int fanout)
    {
        var measurements = new LoadMeasurements(fanout);
        var message = Message();
        measurements.Register(message);
        Parallel.For(0, fanout, target => measurements.Receive(target, message));
        measurements.Pending.ShouldBe(0);
        measurements.Completed.ShouldBe(1);
        measurements.Admitted.ShouldBe(0);
        measurements.Accept(.25);
        measurements.ValidateDrained();
        measurements.TargetCounts.ShouldBe(Enumerable.Repeat(1L, fanout));
        measurements.Admission.Count.ShouldBe(1);
        measurements.Completion.Count.ShouldBe(1);
    }

    [Fact]
    public void Identity_duplicate_and_unknown_delivery_guards_preserve_valid_tracking()
    {
        var measurements = new LoadMeasurements(2);
        var message = Message();
        measurements.Register(message);
        Should.Throw<InvalidOperationException>(() => { _ = measurements.Register(message); });
        measurements.Attempts.ShouldBe(1);
        measurements.Pending.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => measurements.Receive(0, message with { }));
        Should.Throw<ArgumentOutOfRangeException>(() => measurements.Receive(2, message));
        measurements.TargetCounts.ShouldBe(new long[] { 0, 0 });
        measurements.Receive(0, message);
        Should.Throw<InvalidOperationException>(() => measurements.Receive(0, message));
        measurements.TargetCounts.ShouldBe(new long[] { 1, 0 });
        measurements.Receive(1, message);
        measurements.Accept(1);
        Should.Throw<InvalidOperationException>(() => measurements.Receive(1, message));
        measurements.ValidateDrained();
        measurements.Completed.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Declined_admission_releases_tracking_without_completion_samples(bool canceled)
    {
        var measurements = new LoadMeasurements(2);
        var message = Message();
        measurements.Register(message);
        measurements.Decline(message, canceled);
        measurements.Pending.ShouldBe(0);
        measurements.Attempts.ShouldBe(1);
        measurements.Canceled.ShouldBe(canceled ? 1 : 0);
        measurements.Rejected.ShouldBe(canceled ? 0 : 1);
        measurements.Completed.ShouldBe(0);
        measurements.Completion.Count.ShouldBe(0);
        measurements.Admission.Count.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => measurements.Receive(0, message));
        if (canceled) measurements.ValidateDrained();
        else Should.Throw<InvalidOperationException>(measurements.ValidateDrained);
    }

    [Fact]
    public void Tracking_bound_can_be_reused_after_all_targets_finish()
    {
        var measurements = new LoadMeasurements(1, trackingLimit: 2);
        var first = Message();
        var second = Message();
        var third = Message();
        measurements.Register(first);
        measurements.Register(second);
        Should.Throw<InvalidOperationException>(() => { _ = measurements.Register(third); });
        measurements.PeakTracked.ShouldBe(2);
        measurements.Pending.ShouldBe(2);
        measurements.Attempts.ShouldBe(2);
        measurements.Accept(0);
        measurements.Accept(0);
        measurements.Receive(0, first);
        measurements.Receive(0, second);
        measurements.Register(third);
        measurements.Accept(0);
        measurements.Receive(0, third);
        measurements.ValidateDrained();
        measurements.Completed.ShouldBe(3);
        measurements.Pending.ShouldBe(0);
        measurements.PeakTracked.ShouldBe(2);
    }

    [Fact]
    public async Task Warmup_waits_for_all_targets_but_is_excluded_from_measurement()
    {
        var measurements = new LoadMeasurements(2);
        var message = Message();
        var completed = measurements.Register(message, warmup: true).ShouldNotBeNull();
        measurements.Receive(0, message);
        completed.IsCompleted.ShouldBeFalse();
        measurements.Receive(1, message);
        await completed.WaitAsync(TimeSpan.FromSeconds(5));
        measurements.Accept(1, warmup: true);
        measurements.ValidateDrained();
        measurements.Pending.ShouldBe(0);
        measurements.Attempts.ShouldBe(0);
        measurements.Admitted.ShouldBe(0);
        measurements.Completed.ShouldBe(0);
        measurements.TargetCounts.ShouldBe(new long[] { 0, 0 });
        measurements.Admission.Count.ShouldBe(0);
        measurements.Completion.Count.ShouldBe(0);
    }

    private static FlowMessage Message() => FlowMessage.Create(FlowValue.From(42));
}
