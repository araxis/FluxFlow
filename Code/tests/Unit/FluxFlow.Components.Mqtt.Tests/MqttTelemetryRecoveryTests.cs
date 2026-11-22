using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Fact]
    public async Task Telemetry_workflow_restarts_with_the_same_shared_broker_and_http_resources()
    {
        await using var broker = await Broker.Start();
        var received = new BufferBlock<JsonElement>();
        await using var api = CreateTelemetryApi(async context =>
        {
            received.Post(await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted));
            await context.Response.WriteAsync("accepted");
        });
        await api.StartAsync();
        try
        {
            var host = CreateTelemetryHost(broker, api.Urls.Single());
            await using var owner = (IAsyncDisposable)host;
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            await host.StartAsync().WaitAsync(Deadline);
            var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("telemetry");
            var resource = host.Services.GetRequiredFlowResource<MqttClientResource>("broker");
            var first = workflow.Services.GetRequiredKeyedService<IFlowNode>("telemetry.receive");
            try
            {
                await WaitForTelemetrySubscription(events);
                await PublishAndReceive();
                await workflow.StopAsync().WaitAsync(Deadline);
                first.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                resource.IsConnected.ShouldBeFalse();
                await workflow.StartAsync().WaitAsync(Deadline);
                using var restartedEvents = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
                await WaitForTelemetrySubscription(restartedEvents);
                workflow.Services.GetRequiredKeyedService<IFlowNode>("telemetry.receive").ShouldNotBeSameAs(first);
                host.Services.GetRequiredFlowResource<MqttClientResource>("broker").ShouldBeSameAs(resource);
                await PublishAndReceive();
            }
            finally { await host.StopAsync().WaitAsync(Deadline); }
            resource.IsConnected.ShouldBeFalse();
        }
        finally { await api.StopAsync(); }

        async Task PublishAndReceive()
        {
            await broker.Publish("sensors/sensor-7/temperature", Encoding.UTF8.GetBytes("23.5"));
            (await received.ReceiveAsync(Deadline)).GetProperty("temperature").GetDecimal().ShouldBe(23.5m);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Telemetry_retries_preserve_readings_and_route_exhausted_transport_errors(bool exhaust, bool disconnect)
    {
        await using var broker = await Broker.Start();
        var received = new BufferBlock<(string Body, string MessageId)>();
        var attempts = 0;
        await using var api = CreateTelemetryApi(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.Post((await reader.ReadToEndAsync(context.RequestAborted),
                context.Request.Headers["X-Flow-Message-Id"].ToString()));
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt <= (exhaust ? 3 : 2))
            {
                if (disconnect) context.Abort();
                else context.Response.StatusCode = 503;
            }
            else await context.Response.WriteAsync("accepted");
        });
        await api.StartAsync();
        try
        {
            var host = CreateTelemetryHost(broker, api.Urls.Single(), new Dictionary<string, string?>
            {
                ["FluxFlow:Resources:api:Retry:MaxRetryAttempts"] = "2",
                ["FluxFlow:Resources:api:Retry:Delay"] = "00:00:00.010",
                ["FluxFlow:Resources:api:Retry:UseJitter"] = "false",
                ["FluxFlow:Resources:api:Retry:RetryUnsafeMethods"] = "true",
                ["FluxFlow:Workflows:telemetry:send:Error"] = "errors.Input",
                ["FluxFlow:Workflows:telemetry:errors:Type"] = "mapper",
                ["FluxFlow:Workflows:telemetry:errors:Engine"] = "jsonata",
                ["FluxFlow:Workflows:telemetry:errors:Expression"] = "$"
            });
            await using var owner = (IAsyncDisposable)host;
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            using var incoming = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Output"));
            using var requests = host.Services.ObserveOutput(PortAddress.Parse("telemetry.request.Output"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
            using var errors = host.Services.ObserveOutput(PortAddress.Parse("telemetry.errors.Output"));
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                await WaitForTelemetrySubscription(events);
                await broker.Publish("sensors/sensor-7/temperature", Encoding.UTF8.GetBytes("23.5"));
                var original = await incoming.Messages.ReceiveAsync(Deadline);
                var request = await requests.Messages.ReceiveAsync(Deadline);
                var result = await responses.Messages.ReceiveAsync(Deadline);
                Volatile.Read(ref attempts).ShouldBe(3);
                var first = await received.ReceiveAsync(Deadline);
                for (var i = 1; i < 3; i++) (await received.ReceiveAsync(Deadline)).ShouldBe(first);
                first.MessageId.ShouldBe(original.MessageId.Value);
                using var body = JsonDocument.Parse(first.Body);
                body.RootElement.GetProperty("temperature").GetDecimal().ShouldBe(23.5m);
                body.RootElement.GetProperty("messageId").GetString().ShouldBe(original.MessageId.Value);
                body.RootElement.GetProperty("traceId").GetString().ShouldBe(original.TraceId.Value);
                result.TraceId.ShouldBe(original.TraceId);
                result.CorrelationId.ShouldBe(request.CorrelationId);
                result.CausationId.ShouldBe(request.MessageId);
                result.IsError.ShouldBe(disconnect);
                if (disconnect)
                {
                    result.Error!.Code.ShouldBe("node.processing_failed");
                    result.Error.Details!.Json.GetProperty("exceptionType").GetString().ShouldBe(typeof(HttpRequestException).FullName);
                    (await errors.Messages.ReceiveAsync(Deadline)).ShouldBeSameAs(result);
                }
                else result.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(exhaust ? 503 : 200);

                // Exhaustion is local to this reading; the next reading can succeed.
                await broker.Publish("sensors/healthy/temperature", Encoding.UTF8.GetBytes("24"));
                var recovered = await responses.Messages.ReceiveAsync(Deadline);
                recovered.IsError.ShouldBeFalse();
                recovered.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(200);
                recovered.Value.Json.GetProperty("Body").GetString().ShouldBe("accepted");
                using var nextBody = JsonDocument.Parse((await received.ReceiveAsync(Deadline)).Body);
                nextBody.RootElement.GetProperty("deviceId").GetString().ShouldBe("healthy");
                nextBody.RootElement.GetProperty("temperature").GetDecimal().ShouldBe(24m);
                Volatile.Read(ref attempts).ShouldBe(4);
            }
            finally { await host.StopAsync().WaitAsync(Deadline); }
            responses.Messages.TryReceive(out _).ShouldBeFalse();
            errors.Messages.TryReceive(out _).ShouldBeFalse();
            received.TryReceive(out _).ShouldBeFalse();
        }
        finally { await api.StopAsync().WaitAsync(Deadline); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Telemetry_shutdown_drains_retries_or_cancels_them_when_requested(bool cancel)
    {
        await using var broker = await Broker.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var api = CreateTelemetryApi(async context =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                context.Response.StatusCode = 503;
                context.Response.OnCompleted(() => { responded.TrySetResult(); return Task.CompletedTask; });
            }
            else await context.Response.WriteAsync("recovered during shutdown");
        });
        await api.StartAsync();
        try
        {
            var host = CreateTelemetryHost(broker, api.Urls.Single(), new Dictionary<string, string?>
            {
                ["FluxFlow:Resources:api:Timeout"] = "00:02:00",
                ["FluxFlow:Resources:api:Retry:MaxRetryAttempts"] = "1",
                ["FluxFlow:Resources:api:Retry:Delay"] = cancel ? "00:00:30" : "00:00:00.100",
                ["FluxFlow:Resources:api:Retry:UseJitter"] = "false",
                ["FluxFlow:Resources:api:Retry:RetryUnsafeMethods"] = "true"
            });
            using var events = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Events"));
            using var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
            using var shutdown = new CancellationTokenSource();
            try
            {
                await host.StartAsync().WaitAsync(Deadline);
                await WaitForTelemetrySubscription(events);
                await broker.Publish("sensors/sensor-7/temperature", Encoding.UTF8.GetBytes("23.5"));
                await entered.Task.WaitAsync(Deadline);
                shutdown.CancelAfter(Deadline);
                var stopping = host.StopAsync(shutdown.Token);
                host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
                stopping.IsCompleted.ShouldBeFalse();
                release.TrySetResult();
                await responded.Task.WaitAsync(Deadline);
                var application = host.Services.GetRequiredService<FlowApplication>();
                if (cancel)
                {
                    stopping.IsCompleted.ShouldBeFalse();
                    shutdown.Cancel();
                    await Should.ThrowAsync<OperationCanceledException>(() => stopping.WaitAsync(Deadline));
                    await Should.ThrowAsync<OperationCanceledException>(() => application.Completion.WaitAsync(Deadline));
                    Volatile.Read(ref attempts).ShouldBe(1);
                }
                else
                {
                    await stopping.WaitAsync(Deadline);
                    application.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                    var response = await responses.Messages.ReceiveAsync(Deadline);
                    response.IsError.ShouldBeFalse();
                    response.Value.Json.GetProperty("Body").GetString().ShouldBe("recovered during shutdown");
                    Volatile.Read(ref attempts).ShouldBe(2);
                }
                host.Services.GetRequiredKeyedService<FlowWorkflow>("telemetry").Completion.IsCompleted.ShouldBeTrue();
                host.Services.GetRequiredFlowResource<MqttClientResource>("broker").IsConnected.ShouldBeFalse();
            }
            finally
            {
                release.TrySetResult();
                if (shutdown.IsCancellationRequested)
                {
                    var failure = await Should.ThrowAsync<Exception>(() => ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline));
                    IEnumerable<Exception> failures = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [failure];
                    failures.ShouldAllBe(error => error is OperationCanceledException);
                }
                else
                    await ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline);
            }
        }
        finally { await api.StopAsync().WaitAsync(Deadline); }
    }
}
