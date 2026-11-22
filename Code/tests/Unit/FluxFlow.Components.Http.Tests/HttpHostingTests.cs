using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Core.Messaging;
using FluxFlow.Components.Logging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    [Theory]
    [InlineData(200, LogLevel.Information)]
    [InlineData(503, LogLevel.Error)]
    public async Task Hosted_configuration_sends_dynamic_request_and_logs_response(int status, LogLevel level)
    {
        var received = new BufferBlock<(string Method, string Path, string Authorization, string DefaultHeader, string SignalId, string ContentType, JsonElement Body)>();
        await using var server = await Server.Start(async context =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            received.Post((context.Request.Method, context.Request.Path.Value!, context.Request.Headers.Authorization.ToString(),
                context.Request.Headers["X-Default"].ToString(), context.Request.Headers["X-Signal-Id"].ToString(),
                context.Request.ContentType!, body.RootElement.Clone()));
            context.Response.StatusCode = status;
            context.Response.Headers.Append("X-Result", "local-server");
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"accepted\":true}");
        });
        var time = new FakeTimeProvider();
        var logs = new HostLogRecorder();
        var host = CreateHttpHost(server.Address, time, logs);
        await using var owner = (IAsyncDisposable)host;
        try
        {
            received.Count.ShouldBe(0);
            await host.StartAsync().WaitAsync(Deadline);
            using var signals = host.Services.ObserveOutput(PortAddress.Parse("http.clock.Output"));
            using var requests = host.Services.ObserveOutput(PortAddress.Parse("http.request.Output"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("http.send.Output"));
            using var reports = host.Services.ObserveOutput(PortAddress.Parse("http.report.Output"));
            time.Advance(TimeSpan.FromSeconds(1));

            var signal = await signals.Messages.ReceiveAsync(Deadline);
            var request = await requests.Messages.ReceiveAsync(Deadline);
            var response = await responses.Messages.ReceiveAsync(Deadline);
            var report = await reports.Messages.ReceiveAsync(Deadline);
            var sent = await received.ReceiveAsync(Deadline);
            sent.Method.ShouldBe("POST");
            sent.Path.ShouldBe("/api/measurements/" + signal.MessageId.Value);
            sent.SignalId.ShouldBe(signal.MessageId.Value);
            sent.Authorization.ShouldBe("Bearer local-test-token");
            sent.DefaultHeader.ShouldBe("configured");
            sent.ContentType.ShouldStartWith("application/json");
            sent.Body.GetProperty("signalId").GetString().ShouldBe(signal.MessageId.Value);
            sent.Body.GetProperty("traceId").GetString().ShouldBe(signal.TraceId.Value);
            sent.Body.GetProperty("value").GetInt32().ShouldBe(42);
            sent.Body.GetProperty("active").GetBoolean().ShouldBeTrue();
            request.CausationId!.Value.Value.ShouldBe(signal.MessageId.Value);
            response.CausationId!.Value.Value.ShouldBe(request.MessageId.Value);
            response.TraceId.ShouldBe(signal.TraceId);
            response.CorrelationId.ShouldBe(signal.CorrelationId); // Scheduler signals have no request/reply correlation.
            response.IsError.ShouldBeFalse(response.Error?.Message);
            var result = response.Value.ShouldBeOfType<HttpResponse>();
            result.StatusCode.ShouldBe(status);
            result.Body.ShouldBe("{\"accepted\":true}");
            result.Headers["X-Result"].ShouldBe(["local-server"]);
            report.IsError.ShouldBeFalse(report.Error?.Message);
            report.Value.ShouldBeOfType<LogRequest>();
            report.TraceId.ShouldBe(signal.TraceId);
            report.CorrelationId.ShouldBe(signal.CorrelationId);

            await StopHttpHost(host);
            var entry = logs.Entries.Single(item => item.Category == "HttpScenario");
            entry.Level.ShouldBe(level);
            entry.Message.ShouldBe($"HTTP {status}: {{\"accepted\":true}}");
            entry.State["{OriginalFormat}"].ShouldBe("HTTP {StatusCode}: {Body}");
            entry.State["StatusCode"].ShouldBeOfType<long>().ShouldBe(status);
            entry.Attributes["traceId"].ShouldBe(signal.TraceId.Value);
            entry.Attributes["responseMessageId"].ShouldBe(response.MessageId.Value);
            entry.Exception.ShouldBeNull();
            received.Count.ShouldBe(0); // Both declarations of request -> send create only one link.
            reports.Messages.TryReceive(out _).ShouldBeFalse();
        }
        finally { await StopHttpHost(host); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_configuration_routes_transport_failure_or_timeout_and_recovers(bool timeout)
    {
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                if (!timeout) { context.Abort(); return; }
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                return;
            }
            await context.Response.WriteAsync("recovered");
        });
        var time = new FakeTimeProvider();
        var logs = new HostLogRecorder();
        var host = CreateHttpHost(server.Address, time, logs, timeout ? "00:00:01" : "00:00:08");
        await using var owner = (IAsyncDisposable)host;
        try
        {
            await host.StartAsync().WaitAsync(Deadline);
            using var requests = host.Services.ObserveOutput(PortAddress.Parse("http.request.Output"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("http.send.Output"));
            using var reports = host.Services.ObserveOutput(PortAddress.Parse("http.report.Output"));
            time.Advance(TimeSpan.FromSeconds(1));
            await entered.Task.WaitAsync(Deadline);
            var request = await requests.Messages.ReceiveAsync(Deadline);
            var failure = await responses.Messages.ReceiveAsync(Deadline);
            failure.IsError.ShouldBeTrue();
            failure.Error!.Code.ShouldBe("node.processing_failed");
            failure.Error.Details!.Json.GetProperty("exceptionType").GetString().ShouldBe(
                timeout ? typeof(TaskCanceledException).FullName : typeof(HttpRequestException).FullName);
            failure.TraceId.ShouldBe(request.TraceId);
            failure.CausationId!.Value.Value.ShouldBe(request.MessageId.Value);
            (await reports.Messages.ReceiveAsync(Deadline)).ShouldBeSameAs(failure);
            // Error links route the original error. Logger forwards it; only LogRequest values produce logs.
            logs.Entries.Where(item => item.Category == "HttpScenario").ShouldBeEmpty();
            host.Services.GetRequiredService<FlowApplication>().Completion.IsCompleted.ShouldBeFalse();

            time.Advance(TimeSpan.FromSeconds(1));
            (await responses.Messages.ReceiveAsync(Deadline)).Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(200);
            (await reports.Messages.ReceiveAsync(Deadline)).Value.ShouldBeOfType<LogRequest>();
            await StopHttpHost(host);
            Volatile.Read(ref calls).ShouldBe(2);
            logs.Entries.Single(item => item.Category == "HttpScenario").Message.ShouldBe("HTTP 200: recovered");
            reports.Messages.TryReceive(out _).ShouldBeFalse();
        }
        finally { await StopHttpHost(host); }
    }

    [Fact]
    public async Task Hosted_configuration_drains_in_flight_HTTP_request_before_disposal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            await context.Response.WriteAsync("finished during shutdown");
        });
        var time = new FakeTimeProvider();
        var logs = new HostLogRecorder();
        var host = CreateHttpHost(server.Address, time, logs);
        System.Net.Http.HttpClient client;
        await using ((IAsyncDisposable)host)
        {
            client = host.Services.GetRequiredFlowResource<System.Net.Http.HttpClient>("api");
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                using var reports = host.Services.ObserveOutput(PortAddress.Parse("http.report.Output"));
                time.Advance(TimeSpan.FromSeconds(1));
                await entered.Task.WaitAsync(Deadline);
                var stopping = StopHttpHost(host);
                try
                {
                    host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
                    stopping.IsCompleted.ShouldBeFalse();
                }
                finally { release.TrySetResult(); }
                await stopping;
                (await reports.Messages.ReceiveAsync(Deadline)).Value.ShouldBeOfType<LogRequest>();
                logs.Entries.Single(item => item.Category == "HttpScenario").Message.ShouldBe("HTTP 200: finished during shutdown");
                host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested.ShouldBeTrue();
                host.Services.GetRequiredService<FlowApplication>().Completion.IsCompletedSuccessfully.ShouldBeTrue();
                using var response = await client.GetAsync(server.Address).WaitAsync(Deadline);
                response.IsSuccessStatusCode.ShouldBeTrue(); // Stop drains; the provider still owns the resource.
            }
            finally
            {
                release.TrySetResult();
                await StopHttpHost(host);
            }
        }
        await Should.ThrowAsync<ObjectDisposedException>(() => client.GetAsync(server.Address));
    }

    private static IHost CreateHttpHost(Uri server, FakeTimeProvider time, HostLogRecorder logs, string timeout = "00:00:08")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpHosting.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FluxFlow:Resources:api:BaseAddress"] = new Uri(server, "api/").AbsoluteUri,
                ["FluxFlow:Resources:api:Timeout"] = timeout
            });
        builder.Logging.AddProvider(logs);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = Deadline);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddScheduler().AddMapper().AddJsonataExpressionEngine().AddHttp().AddLogger();
        builder.Services.AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow"));
        builder.Services.AddFluxFlow();
        return builder.Build();
    }

    private static async Task StopHttpHost(IHost host)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        await host.StopAsync(deadline.Token);
    }

    private sealed record HostLogEntry(DateTimeOffset Timestamp, string Category, LogLevel Level, EventId EventId,
        string Message, Dictionary<string, object?> State, Dictionary<string, object?> Attributes, Exception? Exception);

    private sealed class HostLogRecorder : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<HostLogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class Recorder(HostLogRecorder owner, string category) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var attributes = new Dictionary<string, object?>();
                owner._scopes.ForEachScope((scope, values) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> items)
                        foreach (var item in items) values[item.Key] = item.Value;
                }, attributes);
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary() : [];
                owner.Entries.Enqueue(new(DateTimeOffset.UtcNow, category, level, eventId, formatter(state, exception), values, attributes, exception));
            }
        }
    }
}
