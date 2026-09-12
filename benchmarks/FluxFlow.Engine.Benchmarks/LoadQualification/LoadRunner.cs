using System.Diagnostics;
using System.Collections.Immutable;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

internal static class LoadRunner
{
    internal static async Task<LoadReport> RunAsync(LoadOptions options, CancellationToken cancellationToken = default)
    {
        options.Validate();
        using var tracing = new LoadTracing(options.Tracing, options.InputTraceContext);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var measurements = new LoadMeasurements(options.Fanout, options.InFlightBound);
        var value = LoadPayload.Create(options);
        var payloadFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
        var trace = TraceId.New();
        var headers = options.InputTraceContext
            ? ImmutableDictionary<string, string>.Empty
                .Add("traceparent", "00-11111111111111111111111111111111-2222222222222222-01")
                .Add("tracestate", "load=qualification")
            : null;
        await using var graph = await LoadGraph.StartAsync(options, measurements);
        for (var index = 0; index < options.WarmupMessages; index++)
        {
            var message = FlowMessage.Create(value, traceId: trace, headers: headers);
            var completed = measurements.Register(message, warmup: true)!;
            if (!await graph.SendAsync(message, deadline.Token)) throw new InvalidOperationException("Warmup was rejected.");
            await completed.WaitAsync(deadline.Token);
        }
        await WaitForIdleAsync();
        var heapBefore = CollectHeap();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        var samples = new List<LoadSample>(512);
        long samplerAllocated = 0;
        var monitor = SampleAsync();
        if (options.MessageLimit == 0) sending.CancelAfter(TimeSpan.FromSeconds(options.Seconds));
        long scheduled = 0;
        double producingSeconds;
        try
        {
            var producers = Enumerable.Range(0, options.Producers).Select(_ => Task.Run(ProduceAsync)).ToArray();
            await Task.WhenAll(producers).WaitAsync(deadline.Token);
            producingSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            await WaitForIdleAsync();
        }
        finally
        {
            sending.Cancel();
            sampling.Cancel();
            await monitor;
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        var queues = graph.Queues();
        measurements.ValidateDrained();
        if (measurements.Completed == 0) throw new InvalidOperationException("The load interval completed no messages.");
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var cpu = process.TotalProcessorTime - cpuBefore;
        var heapAfter = CollectHeap();
        await graph.StopAsync(deadline.Token);
        var traceReport = tracing.Validate(checked((measurements.Completed + options.WarmupMessages) * options.Hops));
        return new(options, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount, GCSettings.IsServerGC, measurements.Attempts, measurements.Admitted,
            measurements.Canceled, measurements.Rejected, measurements.Completed, measurements.TargetCounts,
            producingSeconds, (elapsed.TotalSeconds - producingSeconds) * 1000, elapsed.TotalSeconds,
            measurements.Completed / elapsed.TotalSeconds, cpu.TotalMilliseconds, allocated, samplerAllocated,
            measurements.Admission.Summarize(), measurements.Completion.Summarize(), measurements.PeakTracked,
            options.InFlightBound, measurements.Pending, queues, heapBefore, heapAfter, samples.ToArray(), payloadFingerprint, traceReport);

        async Task ProduceAsync()
        {
            try
            {
                while (!sending.IsCancellationRequested)
                {
                    if (options.MessageLimit != 0 && Interlocked.Increment(ref scheduled) > options.MessageLimit) return;
                    var message = FlowMessage.Create(value, traceId: trace, headers: headers);
                    var attempt = Stopwatch.GetTimestamp();
                    _ = measurements.Register(message);
                    try
                    {
                        if (await graph.SendAsync(message, sending.Token))
                            measurements.Accept(Stopwatch.GetElapsedTime(attempt).TotalMilliseconds);
                        else
                        {
                            measurements.Decline(message, canceled: false);
                            throw new InvalidOperationException("Reliable load admission was unexpectedly rejected.");
                        }
                    }
                    catch (OperationCanceledException) when (sending.IsCancellationRequested)
                    {
                        measurements.Decline(message, canceled: true);
                        return;
                    }
                }
            }
            catch { sending.Cancel(); throw; }
        }

        async Task WaitForIdleAsync()
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var snapshot = graph.Queues();
                if (measurements.Pending == 0 && snapshot.ComponentQueued == 0 && snapshot.ActiveHandlers == 0 &&
                    snapshot.OptionalPortPending.GetValueOrDefault() == 0 && tracing.Active == 0) return;
                await Task.Delay(1, deadline.Token);
            }
        }

        async Task SampleAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            try
            {
                do
                {
                    var probeAllocated = GC.GetAllocatedBytesForCurrentThread();
                    process.Refresh();
                    if (samples.Count < 512) samples.Add(new(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        GC.GetTotalMemory(forceFullCollection: false), process.PrivateMemorySize64,
                        measurements.Pending, measurements.Admitted, measurements.Completed,
                        measurements.TargetCounts, graph.Queues()));
                    samplerAllocated += GC.GetAllocatedBytesForCurrentThread() - probeAllocated;
                } while (await timer.WaitForNextTickAsync(sampling.Token));
            }
            catch (OperationCanceledException) when (sampling.IsCancellationRequested) { }
        }
    }

    private static long CollectHeap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: false);
    }
}

internal sealed record LoadSample(double ElapsedMilliseconds, long ManagedBytes, long PrivateBytes,
    int TrackedMessages, long Admitted, long Completed, long[] TargetCounts, QueueSnapshot Queues);

internal sealed record LoadReport(LoadOptions Configuration, string Framework, int ProcessorCount, bool ServerGc,
    long Attempts, long Admitted, long Canceled, long Rejected, long Completed, long[] TargetCounts,
    double ProducingSeconds, double DrainMilliseconds, double TotalSeconds, double CompletedPerSecond,
    double CpuMilliseconds, long AllocatedBytesIncludingHarness, long SamplerAllocatedBytes, LatencySummary AdmissionLatency,
    LatencySummary AllTargetsLatency, int PeakTrackedMessages, int TrackingBound, int PendingAfterDrain,
    QueueSnapshot DrainedQueues, long ManagedBytesBefore, long ManagedBytesAfterDrain, LoadSample[] Samples, string PayloadSha256,
    LoadTraceReport Tracing)
{
    public string OperatingSystem => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public string ProcessArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public bool TelemetryListenersConfiguredByHarness => Configuration.Tracing != "off";
    public string LatencySemantics => "Individual messages; includes admission and all-target completion; logarithmic bucket upper bounds, 64 buckets per doubling";
    public string MemorySemantics => "Whole workload including harness; forced collection after warmup and idle drain only; graph remains allocated; samples are bounded and counters are not transactional";
}
