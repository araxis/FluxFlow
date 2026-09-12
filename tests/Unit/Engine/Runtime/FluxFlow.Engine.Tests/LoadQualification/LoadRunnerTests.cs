using System.Text.Json;
using FluxFlow.Engine.Benchmarks.LoadQualification;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoadTraceCollection
{
    public const string Name = "Load tracing listeners";
}

[Collection(LoadTraceCollection.Name)]
public sealed class LoadRunnerTests
{
    [Theory]
    [InlineData(false, false, "off")]
    [InlineData(false, true, "off")]
    [InlineData(true, false, "off")]
    [InlineData(true, true, "off")]
    [InlineData(false, false, "unsampled")]
    [InlineData(false, true, "unsampled")]
    [InlineData(true, false, "unsampled")]
    [InlineData(true, true, "unsampled")]
    [InlineData(false, false, "propagation")]
    [InlineData(false, true, "propagation")]
    [InlineData(true, false, "propagation")]
    [InlineData(true, true, "propagation")]
    [InlineData(false, false, "recorded")]
    [InlineData(false, true, "recorded")]
    [InlineData(true, false, "recorded")]
    [InlineData(true, true, "recorded")]
    public async Task Traced_runtime_load_has_exact_bounded_observation_and_delivery(bool updates, bool slow, string mode)
    {
        var result = await LoadRunner.RunAsync(new()
        {
            Updates = updates, Hops = 2, Fanout = 2, Capacity = 4, Producers = 3,
            PayloadBytes = 16, WarmupMessages = 4, MessageLimit = 128,
            SlowTargetMilliseconds = slow ? 1 : 0, Tracing = mode, InputTraceContext = true
        }).WaitAsync(TimeSpan.FromSeconds(30));
        result.Completed.ShouldBe(128);
        result.Admitted.ShouldBe(128);
        result.TargetCounts.ShouldBe(new long[] { 128, 128 });
        result.Canceled.ShouldBe(0);
        result.Rejected.ShouldBe(0);
        result.PendingAfterDrain.ShouldBe(0);
        result.DrainedQueues.ShouldBe(new QueueSnapshot(0, 0, updates ? 0 : null));
        result.TelemetryListenersConfiguredByHarness.ShouldBe(mode != "off");
        var tracing = result.Tracing;
        tracing.Mode.ShouldBe(mode);
        tracing.ExpectedOperationsIncludingWarmup.ShouldBe(264);
        tracing.SampleCallbacks.ShouldBe(mode == "off" ? 0 : 264);
        tracing.Started.ShouldBe(mode is "off" or "unsampled" ? 0 : 264);
        tracing.Stopped.ShouldBe(tracing.Started);
        tracing.FullDataStarted.ShouldBe(mode == "recorded" ? 264 : 0);
        tracing.CorrelatedStarted.ShouldBe(tracing.Started);
        tracing.ActiveAfterDrain.ShouldBe(0);
        tracing.PeakActive.ShouldBeInRange(tracing.Started == 0 ? 0 : 1, 2);
        result.PeakTrackedMessages.ShouldBeInRange(1, result.TrackingBound);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        json.RootElement.GetProperty("Tracing").GetProperty("Mode").GetString().ShouldBe(mode);
        json.RootElement.GetProperty("Configuration").GetProperty("InputTraceContext").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Update_load_drains_when_prepared_output_staging_fills()
    {
        var result = await LoadRunner.RunAsync(new()
        {
            Updates = true, Hops = 8, Fanout = 2, Capacity = 4, Producers = 4,
            PayloadBytes = 16, WarmupMessages = 4, MessageLimit = 512, SlowTargetMilliseconds = 1
        }).WaitAsync(TimeSpan.FromSeconds(30));
        result.Completed.ShouldBe(512);
        result.Admitted.ShouldBe(512);
        result.TargetCounts.ShouldBe(new long[] { 512, 512 });
        result.Rejected.ShouldBe(0);
        result.Canceled.ShouldBe(0);
        result.PendingAfterDrain.ShouldBe(0);
        result.DrainedQueues.ShouldBe(new QueueSnapshot(0, 0, 0));
        result.PeakTrackedMessages.ShouldBeInRange(1, result.TrackingBound);
        result.TrackingBound.ShouldBe(188);
    }

    [Theory]
    [InlineData(false, false, "json")]
    [InlineData(false, true, "json")]
    [InlineData(true, false, "json")]
    [InlineData(true, true, "json")]
    [InlineData(false, false, "binary")]
    [InlineData(false, true, "binary")]
    [InlineData(true, false, "binary")]
    [InlineData(true, true, "binary")]
    public async Task Runtime_load_drains_exactly_once_on_both_execution_paths(bool updates, bool slow, string payloadKind)
    {
        var options = new LoadOptions
        {
            Updates = updates, Hops = 2, Fanout = 2, Capacity = 4, Producers = 3,
            PayloadBytes = 16, PayloadKind = payloadKind, WarmupMessages = 4, MessageLimit = 128, SlowTargetMilliseconds = slow ? 1 : 0
        };
        var result = await LoadRunner.RunAsync(options).WaitAsync(TimeSpan.FromSeconds(30));
        result.Attempts.ShouldBe(128);
        result.Admitted.ShouldBe(128);
        result.Completed.ShouldBe(128);
        result.TargetCounts.ShouldBe(new long[] { 128, 128 });
        result.Canceled.ShouldBe(0);
        result.Rejected.ShouldBe(0);
        result.PendingAfterDrain.ShouldBe(0);
        result.DrainedQueues.ShouldBe(new QueueSnapshot(0, 0, updates ? 0 : null));
        result.PeakTrackedMessages.ShouldBeInRange(1, result.TrackingBound);
        result.AdmissionLatency.Samples.ShouldBe(128);
        result.AllTargetsLatency.Samples.ShouldBe(128);
        result.AllTargetsLatency.P99UpperMilliseconds.ShouldBeGreaterThan(0);
        result.Samples.Length.ShouldBeInRange(1, 512);
        result.Samples.ShouldAllBe(sample => sample.TrackedMessages <= result.TrackingBound);
        result.ManagedBytesBefore.ShouldBeGreaterThan(0);
        result.ManagedBytesAfterDrain.ShouldBeGreaterThan(0);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        json.RootElement.GetProperty("Configuration").GetProperty("Updates").GetBoolean().ShouldBe(updates);
        json.RootElement.GetProperty("Configuration").GetProperty("Hops").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("Configuration").GetProperty("MessageLimit").GetInt64().ShouldBe(128);
        json.RootElement.GetProperty("Configuration").GetProperty("PayloadKind").GetString().ShouldBe(payloadKind);
        var expectedPayload = LoadPayload.Create(options);
        var expectedFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expectedPayload.ToString())));
        json.RootElement.GetProperty("PayloadSha256").GetString().ShouldBe(expectedFingerprint);
    }
}
