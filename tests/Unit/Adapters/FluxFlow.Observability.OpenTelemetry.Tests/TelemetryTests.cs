using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace FluxFlow.Observability.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public async Task Host_stop_flushes_pending_logs_before_service_provider_disposal()
    {
        var original = new InvalidOperationException("pending batch");
        using var exporter = new ShutdownLogExporter();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFluxFlowTelemetry().WithLogging(logging => logging.AddProcessor(
            new BatchLogRecordExportProcessor(exporter, scheduledDelayMilliseconds: 60_000)));
        using var host = builder.Build();
        await host.StartAsync();
        await using var node = new Probe(original);
        var buffer = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(buffer);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await buffer.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        node.Complete();
        await node.Completion;
        exporter.Exceptions.ShouldBeEmpty();
        await host.StopAsync();
        exporter.Exceptions.ShouldContain(original);
    }

    private sealed class ShutdownLogExporter : BaseExporter<LogRecord>
    {
        public List<Exception> Exceptions { get; } = [];
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var log in batch) if (log.Exception is { } exception) Exceptions.Add(exception);
            return ExportResult.Success;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standard_sdk_exports_traces_metrics_and_original_logs(bool sample)
    {
        var spans = new List<Activity>();
        var metrics = new List<Metric>();
        var logs = new List<LogRecord>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFluxFlowTelemetry()
            .ConfigureResource(resource => resource.AddService("fluxflow-test"))
            .WithTracing(tracing => tracing.SetSampler(sample ? new AlwaysOnSampler() : new AlwaysOffSampler()).AddInMemoryExporter(spans))
            .WithMetrics(meter => meter.AddInMemoryExporter(metrics))
            .WithLogging(logging => logging.AddInMemoryExporter(logs));
        using var host = builder.Build();
        await host.StartAsync();
        var original = new InvalidOperationException("conversion", new ArgumentException("inner"));
        await using var node = new Probe(original);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: new Dictionary<string, string>
        { ["traceparent"] = "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01" });
        await node.Input.SendAsync(input);
        (await node.Output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await node.Output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion;
        host.Services.GetRequiredService<TracerProvider>().ForceFlush();
        host.Services.GetRequiredService<MeterProvider>().ForceFlush();
        host.Services.GetRequiredService<LoggerProvider>().ForceFlush();
        var failureLog = logs.Single(log => log.Exception == original);
        failureLog.Exception.ShouldBeSameAs(original);
        if (sample)
        {
            var failed = spans.Single(span => span.Status == ActivityStatusCode.Error);
            failed.TraceId.ToString().ShouldBe("1234567890abcdef1234567890abcdef");
            failed.ParentSpanId.ToString().ShouldBe("1234567890abcdef");
            var exceptionEvent = failed.Events.Single(e => e.Name == "exception");
            exceptionEvent.Tags.Single(t => t.Key == "exception.stacktrace").Value!.ToString()!.ShouldContain("inner");
            failureLog.TraceId.ShouldBe(failed.TraceId);
            failureLog.SpanId.ShouldBe(failed.SpanId);
        }
        else spans.ShouldBeEmpty();
        CheckMetrics(metrics);
        await host.StopAsync();
    }

    private static void CheckMetrics(List<Metric> metrics)
    {
        long completed = 0, active = 0, exceptions = 0;
        double duration = 0;
        foreach (var metric in metrics)
        {
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                if (metric.Name == "fluxflow.processing.completed") completed += point.GetSumLong();
                if (metric.Name == "fluxflow.processing.active") active += point.GetSumLong();
                if (metric.Name == "fluxflow.exceptions") exceptions += point.GetSumLong();
                if (metric.Name == "fluxflow.processing.duration") duration += point.GetHistogramSum();
            }
        }
        completed.ShouldBe(2);
        active.ShouldBe(0);
        exceptions.ShouldBe(1);
        duration.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Otlp_http_exporters_send_all_three_signals()
    {
        var handler = new CaptureHandler();
        var builder = Host.CreateApplicationBuilder();
        void Export(OtlpExporterOptions options, string signal)
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri($"http://collector.test/v1/{signal}");
            options.HttpClientFactory = () => new HttpClient(handler, disposeHandler: false);
        }
        builder.Services.AddFluxFlowTelemetry()
            .WithTracing(t => t.AddOtlpExporter(o => Export(o, "traces")))
            .WithMetrics(m => m.AddOtlpExporter(o => Export(o, "metrics")))
            .WithLogging(l => l.AddOtlpExporter(o => Export(o, "logs")));
        using var host = builder.Build();
        await host.StartAsync();
        await using var node = new Probe(new InvalidOperationException("otlp"));
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        (await node.Output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        node.Complete();
        await node.Completion;
        host.Services.GetRequiredService<TracerProvider>().ForceFlush().ShouldBeTrue();
        host.Services.GetRequiredService<MeterProvider>().ForceFlush().ShouldBeTrue();
        host.Services.GetRequiredService<LoggerProvider>().ForceFlush().ShouldBeTrue();
        foreach (var path in new[] { "/v1/traces", "/v1/metrics", "/v1/logs" })
        {
            handler.Requests.ShouldContain(request => request.Path == path &&
                request.ContentType == "application/x-protobuf" && request.Bytes > 0);
        }
        await host.StopAsync();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentBag<(string Path, string? ContentType, int Bytes)> Requests { get; } = [];
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => SendAsync(request, cancellationToken).GetAwaiter().GetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, request.Content.Headers.ContentType?.MediaType, bytes.Length));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent([]) };
        }
    }

    private sealed class Probe(Exception exception) : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message)
        {
            if (message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1) throw exception;
            return Task.FromResult(message);
        }
    }
}
