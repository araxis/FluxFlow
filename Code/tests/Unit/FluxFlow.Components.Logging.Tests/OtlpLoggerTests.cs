using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Logging.Tests;

public sealed partial class LoggerTests
{
    private static ServiceProvider Provider(Uri endpoint, bool report = true, string protocol = "HttpProtobuf", string? serviceName = null,
        string timeout = "00:00:00.500", RecordingLogger? applicationLogger = null, string minimum = "Trace")
    {
        var resource = new { Type = "logging.otlp", Endpoint = endpoint, Protocol = protocol, ServiceName = serviceName,
            ExportTimeout = timeout, ReportExportErrors = report, Headers = "Authorization=Bearer test-token,X-Key=a=b", MinimumLogLevel = minimum };
        var json = JsonSerializer.Serialize(new
        {
            Resources = new { collector = resource },
            Workflows = new { demo = new {
                log = new { Type = "logger", Logger = "collector" },
                other = new { Type = "logger", Logger = "collector" },
                applicationLog = new { Type = "logger" }
            } }
        });
        return CreateProvider(json, applicationLogger);
    }

    private static ServiceProvider CreateProvider(string json, RecordingLogger? applicationLogger = null, bool mapper = false)
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddLogger();
        if (applicationLogger is not null) services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(new RecordingProvider(applicationLogger)));
        if (mapper) services.AddMapper().AddJsonataExpressionEngine();
        services.AddFluxFlowApplication(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static FlowMessage Request(string name = "A12") => Message(new
    {
        logLevel = "Information", messageTemplate = "Order {OrderId}", args = new[] { name },
        attributes = new { machine = "line-2", successful = true }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generic_host_starts_loggers_drains_requests_and_disposes_factories(bool report)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var collector = await Collector.Start(reply: async (_, context) =>
        {
            if (report) await release.Task.WaitAsync(context.RequestAborted);
        });
        var json = $$"""
        { "Resources": { "collector": {
            "Type": "logging.otlp", "Endpoint": "{{collector.Endpoint}}",
            "ReportExportErrors": {{(report ? "true" : "false")}}, "ExportTimeout": "00:00:10"
          } },
          "Workflows": { "demo": {
            "log": { "Type": "logger", "Logger": "collector" },
            "applicationLog": { "Type": "logger" }
          } } }
        """;
        var recording = new RecordingLogger();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        builder.Logging.ClearProviders().AddFilter("Microsoft", LogLevel.None)
            .AddProvider(new RecordingProvider(recording));
        builder.Services.AddLogger().AddFluxFlowApplication(builder.Configuration).AddFluxFlow();
        var host = builder.Build();
        ILoggerFactory dedicated;
        ILoggerFactory application;
        try
        {
            await host.StartAsync().WaitAsync(Deadline);
            dedicated = host.Services.GetRequiredFlowResource<ILoggerFactory>("collector");
            application = host.Services.GetRequiredService<ILoggerFactory>();
            collector.Bodies.ShouldBeEmpty();
            var input = host.Services.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input");
            var output = Capture(host.Services.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
            var hostInput = host.Services.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.applicationLog.Input");
            var hostOutput = Capture(host.Services.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.applicationLog.Output"));
            (await hostInput.SendAsync(Request("host")).WaitAsync(Deadline)).ShouldBeTrue();
            (await hostOutput.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            recording.Entries.Count(entry => entry.Message == "Order host").ShouldBe(1);
            collector.Bodies.ShouldBeEmpty();

            (await input.SendAsync(Request("dedicated")).WaitAsync(Deadline)).ShouldBeTrue();
            if (report)
            {
                await collector.Received.Task.WaitAsync(Deadline);
                output.Count.ShouldBe(0);
                using var timeout = new CancellationTokenSource(Deadline);
                var stopping = host.StopAsync(timeout.Token);
                try { stopping.IsCompleted.ShouldBeFalse(); }
                finally { release.TrySetResult(); }
                await stopping.WaitAsync(Deadline);
                (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            }
            else
            {
                (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
                await host.StopAsync().WaitAsync(Deadline);
            }
            host.Services.GetRequiredKeyedService<FlowWorkflow>("demo").Completion.IsCompletedSuccessfully.ShouldBeTrue();
            (await input.SendAsync(Request("after-stop")).WaitAsync(Deadline)).ShouldBeFalse();
            recording.Entries.ShouldNotContain(entry => entry.Message == "Order dedicated");
        }
        finally
        {
            release.TrySetResult();
            try { await host.StopAsync().WaitAsync(Deadline); }
            finally
            {
                // Host disposal flushes and disposes the DI-owned factories, even if a check fails.
                await ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline);
            }
        }

        var records = collector.Bodies.SelectMany(body => Bytes(body, 1))
            .SelectMany(resource => Bytes(resource, 2)).SelectMany(scope => Bytes(scope, 2)).ToArray();
        records.Length.ShouldBe(1);
        var record = Encoding.UTF8.GetString(records[0]);
        record.ShouldContain("Order {OrderId}");
        record.ShouldContain("dedicated");
        Should.Throw<ObjectDisposedException>(() => dedicated.CreateLogger("after-dispose"));
        Should.Throw<ObjectDisposedException>(() => application.CreateLogger("after-dispose"));
    }

    [Theory]
    [InlineData(false, "HttpProtobuf", null)]
    [InlineData(true, "HttpProtobuf", "")]
    [InlineData(true, "HttpProtobuf", "   ")]
    [InlineData(true, "HttpProtobuf", "production")]
    [InlineData(false, "Grpc", null)]
    [InlineData(true, "Grpc", null)]
    [InlineData(true, "Grpc", "production")]
    public async Task Dedicated_pipeline_preserves_log_fields_headers_and_only_explicit_resource_attributes(bool report, string protocol, string? serviceName)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        await using var collector = await Collector.Start(protocol);
        var hostLogger = new RecordingLogger();
        await using var provider = Provider(collector.Endpoint, report, protocol, serviceName, applicationLogger: hostLogger);
        await using var app = await provider.BuildApplicationAsync();
        collector.Bodies.ShouldBeEmpty();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        await app.StopAsync().WaitAsync(Deadline);
        // Standard mode flushes its background batch when the DI-owned factory is disposed.
        await provider.DisposeAsync();
        var body = collector.Bodies.Single();
        var text = Encoding.UTF8.GetString(body);
        text.ShouldContain("Order {OrderId}");
        text.ShouldContain("OrderId");
        text.ShouldContain("A12");
        text.ShouldContain("machine");
        text.ShouldContain("line-2");
        text.ShouldNotContain("telemetry.sdk.");
        text.ShouldNotContain("unknown_service");
        var resourceLogs = Bytes(body, 1).Single();
        var attributes = Bytes(resourceLogs, 1).SelectMany(resource => Bytes(resource, 1)).ToArray();
        if (string.IsNullOrWhiteSpace(serviceName)) attributes.ShouldBeEmpty();
        else
        {
            attributes.Length.ShouldBe(1);
            Encoding.UTF8.GetString(Bytes(attributes[0], 1).Single()).ShouldBe("service.name");
            Encoding.UTF8.GetString(Bytes(Bytes(attributes[0], 2).Single(), 1).Single()).ShouldBe(serviceName);
        }
        var record = Bytes(resourceLogs, 2).SelectMany(scope => Bytes(scope, 2)).Single();
        Bytes(record, 9).Single().Length.ShouldBe(16); // Native trace ID.
        Bytes(record, 10).Single().Length.ShouldBe(8); // Native span ID.
        collector.Headers.Single().ShouldBe(["Bearer test-token", "a=b"]);
        hostLogger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reporting_mode_waits_for_the_collector_response()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var collector = await Collector.Start(reply: async (_, context) => await release.Task.WaitAsync(context.RequestAborted));
        await using var provider = Provider(collector.Endpoint, timeout: "00:00:05");
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        try
        {
            await collector.Received.Task.WaitAsync(Deadline);
            output.Count.ShouldBe(0);
        }
        finally { release.TrySetResult(); }
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData("HttpProtobuf", 400)]
    [InlineData("HttpProtobuf", 503)]
    [InlineData("Grpc", 400)]
    [InlineData("Grpc", 503)]
    public async Task Export_failures_become_errors_and_do_not_fault_the_component(string protocol, int status)
    {
        await using var collector = await Collector.Start(protocol, (_, context) => { context.Response.StatusCode = status; return Task.CompletedTask; });
        await using var provider = Provider(collector.Endpoint, protocol: protocol);
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeTrue();
        result.Error!.Message.ShouldContain("OTLP log export failed");
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_timeout_reports_errors_only_when_enabled(bool report)
    {
        await using var collector = await Collector.Start(reply: async (_, context) => await Task.Delay(Deadline, context.RequestAborted));
        await using var provider = Provider(collector.Endpoint, report, timeout: "00:00:00.150");
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        var result = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        result.IsError.ShouldBe(report);
        await app.StopAsync().WaitAsync(Deadline);
        await provider.DisposeAsync();
        collector.Bodies.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Jsonata_array_supports_partial_export_failure_and_uses_the_selected_logger()
    {
        await using var collector = await Collector.Start(reply: (index, context) => { if (index == 1) context.Response.StatusCode = 400; return Task.CompletedTask; });
        var json = $$"""
        { "Resources": { "collector": { "Type": "logging.otlp", "Endpoint": "{{collector.Endpoint}}", "ReportExportErrors": true } },
          "Workflows": { "demo": {
            "map": { "Type": "mapper", "Engine": "jsonata", "Expression": "[{'logLevel':'Warning','messageTemplate':'First {Id}','args':[id]}, {'logLevel':'Information','messageTemplate':'Second {Id}','args':[id]}]", "Output": "log.Input" },
            "log": { "Type": "logger", "Logger": "collector", "Input": "map.Output" }
          } } }
        """;
        await using var provider = CreateProvider(json, mapper: true);
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.map.Input").SendAsync(Message(new { id = 42 }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        collector.Bodies.Count.ShouldBe(2);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Omitted_destination_uses_only_the_application_logger()
    {
        var hostLogger = new RecordingLogger();
        await using var provider = CreateProvider("""{"Workflows":{"demo":{"log":{"Type":"logger"}}}}""", hostLogger);
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        hostLogger.Entries.Single().Message.ShouldBe("Order A12");
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData("ExportTimeout", "\"00:00:00\"")]
    [InlineData("Protocol", "999")]
    [InlineData("MinimumLogLevel", "999")]
    [InlineData("Endpoint", "\"ftp://localhost/logs\"")]
    [InlineData("Headers", "\"missing-equals\"")]
    [InlineData("Headers", "\"X-Key=value%0D%0AInjected:bad\"")]
    public void Invalid_resource_configuration_fails_atomically(string property, string value)
    {
        var json = $$"""{ "Resources": { "logs": { "Type":"logging.otlp", "Endpoint":"http://localhost/v1/logs", "{{property}}": {{value}} } }, "Workflows": {} }""";
        // Avoid duplicate JSON properties when testing Endpoint.
        if (property == "Endpoint") json = $$"""{ "Resources": { "logs": { "Type":"logging.otlp", "Endpoint": {{value}} } }, "Workflows": {} }""";
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddLogger();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        services.ToArray().ShouldBe(before);
    }

    private sealed class RecordingProvider(RecordingLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    [Fact]
    public async Task Shared_exporter_keeps_concurrent_calls_and_failures_independent()
    {
        await using var collector = await Collector.Start("HttpProtobuf", (_, context) =>
        {
            context.Response.StatusCode = 400;
            return Task.CompletedTask;
        });
        await using var provider = Provider(collector.Endpoint);
        var factory = provider.GetRequiredFlowResource<ILoggerFactory>("collector");
        var tasks = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            var logger = factory.CreateLogger($"parallel-{index}");
            Should.Throw<InvalidOperationException>(() => logger.LogInformation("Item {Index}", index));
        }));
        await Task.WhenAll(tasks).WaitAsync(Deadline);
        collector.Bodies.Count.ShouldBe(12);
        var categories = collector.Bodies.Select(body => Bytes(body, 1).Single())
            .Select(resource => Bytes(resource, 2).Single())
            .Select(scope => Bytes(scope, 1).Single())
            .Select(scope => Encoding.UTF8.GetString(Bytes(scope, 1).Single())).ToArray();
        categories.Distinct().Count().ShouldBe(12);
    }

    [Fact]
    public async Task Intentional_level_filtering_is_not_an_export_error()
    {
        await using var collector = await Collector.Start("HttpProtobuf");
        await using var provider = Provider(collector.Endpoint, minimum: "Warning");
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.log.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.log.Input").SendAsync(Request());
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        collector.Bodies.ShouldBeEmpty();
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Explicit_missing_logger_never_falls_back_to_application_logging()
    {
        await using var provider = CreateProvider("""{"Workflows":{"demo":{"log":{"Type":"logger","Logger":"missing"}}}}""");
        await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
    }

    private sealed class Collector(WebApplication app, Uri endpoint) : IAsyncDisposable
    {
        public Uri Endpoint => endpoint;
        public readonly ConcurrentQueue<byte[]> Bodies = new();
        public readonly ConcurrentQueue<string[]> Headers = new();
        public readonly TaskCompletionSource Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static async Task<Collector> Start(string protocol = "HttpProtobuf", Func<int, HttpContext, Task>? reply = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listen => listen.Protocols = protocol == "Grpc" ? HttpProtocols.Http2 : HttpProtocols.Http1));
            var app = builder.Build();
            Collector? collector = null;
            var count = 0;
            app.Run(async context =>
            {
                using var bytes = new MemoryStream();
                await context.Request.Body.CopyToAsync(bytes);
                var body = bytes.ToArray();
                collector!.Bodies.Enqueue(protocol == "Grpc" ? body[5..] : body);
                collector.Headers.Enqueue([context.Request.Headers.Authorization.ToString(), context.Request.Headers["X-Key"].ToString()]);
                collector.Received.TrySetResult();
                try
                {
                    if (reply is not null) await reply(Interlocked.Increment(ref count), context);
                    if (protocol == "Grpc" && context.Response.StatusCode == 200)
                    {
                        context.Response.ContentType = "application/grpc";
                        context.Response.DeclareTrailer("grpc-status");
                        await context.Response.Body.WriteAsync(new byte[5]);
                        context.Response.AppendTrailer("grpc-status", "0");
                    }
                    else context.Response.ContentType = "application/x-protobuf";
                }
                catch (OperationCanceledException) { }
            });
            await app.StartAsync();
            collector = new Collector(app, new Uri(app.Urls.Single().TrimEnd('/') + "/v1/logs"));
            return collector;
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // Read length-delimited fields from the real OTLP protobuf envelope without depending on SDK internals.
    private static IEnumerable<byte[]> Bytes(byte[] message, int field)
    {
        var position = 0;
        ulong Varint()
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var next = message[position++];
                result |= (ulong)(next & 127) << shift;
                if ((next & 128) == 0) return result;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }
        while (position < message.Length)
        {
            var tag = Varint();
            switch (tag & 7)
            {
                case 0: Varint(); break;
                case 1: position += 8; break;
                case 5: position += 4; break;
                case 2:
                    var length = checked((int)Varint());
                    var start = position;
                    position += length;
                    if ((int)(tag >> 3) == field) yield return message[start..position];
                    break;
                default: throw new InvalidDataException("Unexpected protobuf wire type.");
            }
        }
    }
}
