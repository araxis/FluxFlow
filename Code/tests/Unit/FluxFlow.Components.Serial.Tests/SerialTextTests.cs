using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Hosting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serial.Tests;

public sealed class SerialTextTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Receives_fragmented_utf8_crlf_empty_lines_and_preserves_whitespace()
    {
        using var port = new TestPort();
        await using var node = port.Create();
        var output = Capture(node.Output);
        await node.StartAsync().WaitAsync(Deadline);
        var bytes = Encoding.UTF8.GetBytes("  café ☕  \r\n\nlast\npartial");
        foreach (var value in bytes) port.Reads.Add(new byte[] { value });
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("  café ☕  ");
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("");
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("last");
        node.StopProduction();
        await node.Completion.WaitAsync(Deadline);
        output.Count.ShouldBe(0);
        port.Closes.ShouldBe(1);
    }

    [Fact]
    public async Task Partial_lines_survive_idle_timeouts_and_bad_frames_recover()
    {
        using var port = new TestPort();
        await using var node = port.Create(new() { MaxLineBytes = 4 });
        var output = Capture(node.Output);
        await node.StartAsync().WaitAsync(Deadline);
        port.Reads.Add(Encoding.UTF8.GetBytes("ab"));
        port.Reads.Add(new TimeoutException());
        port.Reads.Add(Encoding.UTF8.GetBytes("cd\r\n123456789\nok\n"));
        port.Reads.Add(new byte[] { 0xff, 10 });
        port.Reads.Add(Encoding.UTF8.GetBytes("next\n"));
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("abcd");
        (await output.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("serial.line_too_long");
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("ok");
        (await output.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("serial.invalid_utf8");
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("next");
        node.StopProduction();
        await node.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Byte_loopback_starts_with_a_clean_line_after_Stop(bool stopDuringOversizedLine)
    {
        using var port = new TestPort();
        var frames = new BufferBlock<byte[]>();
        port.BeforeWrite = bytes =>
        {
            frames.Post(bytes);
            foreach (var value in bytes) port.Reads.Add(new byte[] { value });
        };
        for (var run = 0; run < 2; run++)
        {
            await using var node = port.Create(new() { NewLine = "\r\n", MaxLineBytes = 6 });
            var output = Capture(node.Output);
            await node.StartAsync().WaitAsync(Deadline);
            (await node.Input.SendAsync(Request("é🙂"))).ShouldBeTrue();
            (await frames.ReceiveAsync(Deadline)).ShouldBe("é🙂\r\n"u8.ToArray());
            (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("é🙂");

            port.Reads.Add("é🙂!\r\n"u8.ToArray());
            (await output.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("serial.line_too_long");
            (await node.Input.SendAsync(Request("ok"))).ShouldBeTrue();
            (await frames.ReceiveAsync(Deadline)).ShouldBe("ok\r\n"u8.ToArray());
            (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("ok");

            if (run == 0)
                port.ReadThroughIdleTimeout(stopDuringOversizedLine ? "12345678"u8.ToArray() : new byte[] { 0xf0, 0x9f });
            node.StopProduction();
            await node.Completion.WaitAsync(Deadline);
            output.Count.ShouldBe(0);
            frames.Count.ShouldBe(0);
            port.Closes.ShouldBe(run + 1);
        }
        port.Opens.ShouldBe(2);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Stop_drains_accepted_writes_in_order_and_rejects_new_input(string newline)
    {
        using var port = new TestPort();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        port.BeforeWrite = _ => { entered.Set(); release.Wait(Deadline).ShouldBeTrue(); };
        await using var node = port.Create(new() { NewLine = newline });
        var events = Capture(node.Events);
        var output = Capture(node.Output);
        await node.StartAsync().WaitAsync(Deadline);
        var first = Request("first");
        try
        {
            (await node.Input.SendAsync(first)).ShouldBeTrue();
            entered.Wait(Deadline).ShouldBeTrue();
            (await node.Input.SendAsync(Request("second"))).ShouldBeTrue();
            port.ReadThroughIdleTimeout([0xe2, 0x82]);
            node.StopProduction();
            (await node.Input.SendAsync(Request("late"))).ShouldBeFalse();
            node.Completion.IsCompleted.ShouldBeFalse();
            port.Closes.ShouldBe(0);
        }
        finally { release.Set(); }
        await node.Completion.WaitAsync(Deadline);
        port.Writes.ToArray().ShouldBe(new[] { "first" + newline, "second" + newline });
        port.Closes.ShouldBe(1);
        output.Count.ShouldBe(0);
        var written = new List<FlowMessage>();
        while (events.TryReceive(out var message))
            if (((FlowEvent)message.Value).Name == "serial.written") written.Add(message);
        written.Count.ShouldBe(2);
        written[0].CausationId.ShouldBe(first.MessageId);
        written[0].TraceId.ShouldBe(first.TraceId);
    }

    [Theory]
    [InlineData("{\"Text\":\"a\\nb\"}")]
    [InlineData("{\"Text\":\"a\\rb\"}")]
    [InlineData("{\"Text\":\"longer\"}")]
    [InlineData("{\"Text\":null}")]
    [InlineData("{}")]
    public async Task Bad_commands_become_correlated_errors_without_writing(string json)
    {
        using var port = new TestPort();
        await using var node = port.Create(new() { MaxLineBytes = 4 });
        var output = Capture(node.Output);
        await node.StartAsync().WaitAsync(Deadline);
        using var document = JsonDocument.Parse(json);
        var request = FlowMessage.Create(new JsonFlowValue(document.RootElement));
        await node.Input.SendAsync(request);
        var error = await output.ReceiveAsync(Deadline);
        error.IsError.ShouldBeTrue();
        error.CausationId.ShouldBe(request.MessageId);
        port.Writes.ShouldBeEmpty();
        await node.Input.SendAsync(Request("ok"));
        node.StopProduction();
        await node.Completion.WaitAsync(Deadline);
        port.Writes.ToArray().ShouldBe(new[] { "ok\n" });
    }

    [Theory]
    [InlineData("open")]
    [InlineData("read")]
    [InlineData("write")]
    public async Task Port_failure_faults_execution_and_disposal_closes_once(string operation)
    {
        using var port = new TestPort();
        var failure = new IOException("Disconnected test port");
        if (operation == "open") port.OpenError = failure;
        if (operation == "write") port.BeforeWrite = _ => throw failure;
        var node = port.Create();
        try
        {
            if (operation == "open")
                await Should.ThrowAsync<IOException>(() => node.StartAsync().WaitAsync(Deadline));
            else
            {
                await node.StartAsync().WaitAsync(Deadline);
                if (operation == "read") port.Reads.Add(failure);
                else await node.Input.SendAsync(Request("first"));
            }
            await Should.ThrowAsync<IOException>(() => node.Completion.WaitAsync(Deadline));
            (await node.Input.SendAsync(Request("late"))).ShouldBeFalse();
        }
        finally { await node.DisposeAsync().AsTask().WaitAsync(Deadline); }
        port.Closes.ShouldBe(operation == "open" ? 0 : 1);
        await node.DisposeAsync();
        port.Closes.ShouldBe(operation == "open" ? 0 : 1);
    }

    [Fact]
    public async Task Incoming_error_is_forwarded_unchanged_without_becoming_a_write()
    {
        using var port = new TestPort();
        await using var node = port.Create();
        var output = Capture(node.Output);
        await node.StartAsync().WaitAsync(Deadline);
        var error = FlowMessage.Create(new FlowError("upstream.failed", "Original failure", "test"));
        (await node.Input.SendAsync(error)).ShouldBeTrue();
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        port.Writes.ShouldBeEmpty();
        node.StopProduction();
        await node.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Write_timeout_does_not_retry_or_send_the_next_queued_command()
    {
        using var port = new TestPort();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var transmitted = new ConcurrentQueue<byte>();
        var timeout = new TimeoutException("A partial native write timed out.");
        port.BeforeWrite = bytes =>
        {
            transmitted.Enqueue(bytes[0]);
            entered.Set();
            release.Wait(Deadline).ShouldBeTrue();
            throw timeout;
        };
        await using var node = port.Create();
        var events = Capture(node.Events);
        await node.StartAsync().WaitAsync(Deadline);
        try
        {
            (await node.Input.SendAsync(Request("first"))).ShouldBeTrue();
            entered.Wait(Deadline).ShouldBeTrue();
            (await node.Input.SendAsync(Request("second"))).ShouldBeTrue();
        }
        finally { release.Set(); }
        (await Should.ThrowAsync<TimeoutException>(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(timeout);
        transmitted.ToArray().ShouldBe(new byte[] { (byte)'f' });
        while (events.TryReceive(out var message))
            ((FlowEvent)message.Value).Name.ShouldNotBe("serial.written");
        port.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispose_before_start_finishes_without_opening_port()
    {
        using var port = new TestPort();
        var node = port.Create();
        await node.Input.SendAsync(Request("queued"));
        await node.DisposeAsync().AsTask().WaitAsync(Deadline);
        port.Opens.ShouldBe(0);
        port.Writes.ShouldBeEmpty();
        port.Closes.ShouldBe(0);
        await Should.ThrowAsync<ObjectDisposedException>(() => node.StartAsync().WaitAsync(Deadline));
    }

    [Fact]
    public async Task Host_stop_drains_accepted_writes_before_closing_and_disposing_resource()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddFluxFlow();
        var port = new TestPort();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        port.BeforeWrite = _ => { entered.Set(); release.Wait(Deadline).ShouldBeTrue(); };
        var address = RegisterSerial(builder.Services, "device", () => port);
        var host = builder.Build();
        await using ((IAsyncDisposable)host)
        {
            port.Opens.ShouldBe(0);
            await host.StartAsync().WaitAsync(Deadline);
            port.Opens.ShouldBe(1);
            var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("device");
            var node = (SerialText)workflow.Services.GetRequiredKeyedService<IFlowNode>(address.Value);
            Task stopped;
            try
            {
                (await node.Input.SendAsync(Request("first"))).ShouldBeTrue();
                entered.Wait(Deadline).ShouldBeTrue();
                (await node.Input.SendAsync(Request("second"))).ShouldBeTrue();
                stopped = host.StopAsync();
                (await node.Input.SendAsync(Request("late"))).ShouldBeFalse();
                stopped.IsCompleted.ShouldBeFalse();
                port.Closes.ShouldBe(0);
                port.Disposals.ShouldBe(0);
            }
            finally { release.Set(); }
            await stopped.WaitAsync(Deadline);
            await node.Completion.WaitAsync(Deadline);
            port.Writes.ToArray().ShouldBe(new[] { "first\n", "second\n" });
            port.Closes.ShouldBe(1);
            port.Disposals.ShouldBe(1);
        }
        port.Closes.ShouldBe(1);
        port.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Host_start_failure_preserves_error_and_cleans_up_started_resources()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddFluxFlow();
        var healthy = new TestPort();
        var failure = new IOException("Cannot open simulated port.");
        var broken = new TestPort { OpenError = failure };
        RegisterSerial(builder.Services, "healthy", () => healthy);
        RegisterSerial(builder.Services, "broken", () => broken);
        var host = builder.Build();
        Exception? cleanup = null;
        try
        {
            (await Should.ThrowAsync<IOException>(() => host.StartAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
            healthy.Opens.ShouldBe(1);
            healthy.Closes.ShouldBe(1);
            healthy.Disposals.ShouldBe(1);
            broken.Opens.ShouldBe(0);
            broken.Closes.ShouldBe(0);
            broken.Disposals.ShouldBe(1);
        }
        finally
        {
            cleanup = await Record.ExceptionAsync(() =>
                ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline));
        }
        foreach (var error in cleanup.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions)
            error.ShouldBeSameAs(failure);
        healthy.Disposals.ShouldBe(1);
        broken.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_workflow_restart_recreates_serial_resource_while_other_workflow_keeps_receiving()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddFluxFlow();
        var ports = new List<TestPort>();
        var address = RegisterSerial(builder.Services, "device", () =>
        {
            var port = new TestPort();
            ports.Add(port);
            return port;
        });
        var otherPort = new TestPort();
        var otherAddress = RegisterSerial(builder.Services, "other", () => otherPort);
        var host = builder.Build();
        await using ((IAsyncDisposable)host)
        {
            ports.ShouldBeEmpty();
            await host.StartAsync().WaitAsync(Deadline);
            var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("device");
            var otherWorkflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("other");
            var otherNode = (SerialText)otherWorkflow.Services.GetRequiredKeyedService<IFlowNode>(otherAddress.Value);
            var otherOutput = Capture(otherNode.Output);
            SerialText? previous = null;
            for (var run = 0; run < 2; run++)
            {
                if (run > 0) await workflow.StartAsync().WaitAsync(Deadline);
                var node = (SerialText)workflow.Services.GetRequiredKeyedService<IFlowNode>(address.Value);
                node.ShouldNotBeSameAs(previous);
                ports[run].Opens.ShouldBe(1);
                if (run > 0) ports[run].ShouldNotBeSameAs(ports[run - 1]);
                var output = Capture(node.Output);
                ports[run].Reads.Add(Encoding.UTF8.GetBytes("reading\n"));
                (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("reading");
                await node.Input.SendAsync(Request("command"));
                await workflow.StopAsync().WaitAsync(Deadline);
                ports[run].Writes.ToArray().ShouldBe(new[] { "command\n" });
                ports[run].Closes.ShouldBe(1);
                ports[run].Disposals.ShouldBe(1);
                (await node.Input.SendAsync(Request("late"))).ShouldBeFalse();
                otherPort.Reads.Add(Encoding.UTF8.GetBytes($"still running {run}\n"));
                (await otherOutput.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe($"still running {run}");
                otherWorkflow.Services.GetRequiredKeyedService<IFlowNode>(otherAddress.Value).ShouldBeSameAs(otherNode);
                otherPort.Closes.ShouldBe(0);
                otherPort.Disposals.ShouldBe(0);
                previous = node;
            }
            await host.StopAsync().WaitAsync(Deadline);
            await otherNode.Completion.WaitAsync(Deadline);
            ports.Count.ShouldBe(2);
            otherPort.Opens.ShouldBe(1);
            otherPort.Closes.ShouldBe(1);
            otherPort.Disposals.ShouldBe(1);
        }
        foreach (var port in ports.Append(otherPort))
        {
            port.Closes.ShouldBe(1);
            port.Disposals.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("TelemetrySample.json", "SerialTelemetry", "telemetry", "receive")]
    [InlineData("HttpSample.json", "SerialCommands", "commands", "serial")]
    public async Task Sample_builds_with_native_serial_registration_without_opening_port(
        string file, string section, string workflowName, string component)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, file));
        builder.Services.AddSerial().AddHttp().AddHttpTriggers().AddMapper().AddLogger().AddJsonataExpressionEngine()
            .AddFluxFlowApplication(builder.Configuration.GetRequiredSection(section)).AddFluxFlow();
        var host = builder.Build();
        await using ((IAsyncDisposable)host)
        {
            await host.Services.BuildApplicationAsync();
            var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>(workflowName);
            var port = workflow.Services.GetRequiredKeyedService<System.IO.Ports.SerialPort>(ApplicationAddress.WorkflowResource(workflowName, "port").Value);
            port.IsOpen.ShouldBeFalse();
            port.PortName.ShouldBe("COM3");
            port.BaudRate.ShouldBe(115200);
            workflow.Services.GetRequiredKeyedService<IFlowNode>(ComponentAddress.Create(workflowName, component).Value).ShouldBeOfType<SerialText>();
        }
    }

    [Fact]
    public async Task Telemetry_sample_maps_lines_logs_errors_restarts_and_drains_http_on_shutdown()
    {
        var received = new BufferBlock<(JsonElement Body, string MessageId)>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiBuilder = WebApplication.CreateSlimBuilder();
        apiBuilder.Logging.ClearProviders();
        apiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var api = apiBuilder.Build();
        api.MapPost("/readings", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            received.Post((body, context.Request.Headers["X-Flow-Message-Id"].ToString()));
            var temperature = body.GetProperty("Temperature").GetDecimal();
            if (temperature == -1) context.Abort();
            else
            {
                if (temperature == 99) await release.Task.WaitAsync(context.RequestAborted);
                context.Response.StatusCode = 201;
                await context.Response.WriteAsync("accepted", context.RequestAborted);
            }
        });
        await api.StartAsync().WaitAsync(Deadline);
        try
        {
            var ports = new List<TestPort>();
            var logs = new LogCapture("telemetry.log");
            var host = CreateTelemetryHost(api.Urls.Single(), ports, logs);
            await using ((IAsyncDisposable)host)
            {
                await host.StartAsync().WaitAsync(Deadline);
                var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("telemetry");
                var originalNode = workflow.Services.GetRequiredKeyedService<IFlowNode>("telemetry.receive");
                using (var incoming = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Output")))
                using (var responses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output")))
                {
                    ports.Count.ShouldBe(1);
                    ports[0].Reads.Add(Encoding.UTF8.GetBytes("23.5\r\n"));
                    await AssertDelivery(incoming, responses, 23.5m);

                    ports[0].Reads.Add(Encoding.UTF8.GetBytes("not-a-number\n"));
                    await AssertLoggedError(incoming, responses);
                    received.Count.ShouldBe(0);

                    ports[0].Reads.Add(Encoding.UTF8.GetBytes("-1\n"));
                    await AssertLoggedError(incoming, responses);
                    (await received.ReceiveAsync(Deadline)).Body.GetProperty("Temperature").GetDecimal().ShouldBe(-1m);

                    // A bad reading or HTTP transport error must not prevent a later reading.
                    ports[0].Reads.Add(Encoding.UTF8.GetBytes("24\n"));
                    await AssertDelivery(incoming, responses, 24m);
                }
                await workflow.StopAsync().WaitAsync(Deadline);
                ports[0].Closes.ShouldBe(1);
                ports[0].Disposals.ShouldBe(1);
                await workflow.StartAsync().WaitAsync(Deadline);
                ports.Count.ShouldBe(2);
                ports[1].ShouldNotBeSameAs(ports[0]);
                workflow.Services.GetRequiredKeyedService<IFlowNode>("telemetry.receive").ShouldNotBeSameAs(originalNode);
                using var restartedIncoming = host.Services.ObserveOutput(PortAddress.Parse("telemetry.receive.Output"));
                using var restartedResponses = host.Services.ObserveOutput(PortAddress.Parse("telemetry.send.Output"));
                ports[1].Reads.Add(Encoding.UTF8.GetBytes("25\n"));
                await AssertDelivery(restartedIncoming, restartedResponses, 25m);

                Task stopped;
                try
                {
                    ports[1].Reads.Add(Encoding.UTF8.GetBytes("99\n"));
                    (await received.OutputAvailableAsync().WaitAsync(Deadline)).ShouldBeTrue();
                    stopped = host.StopAsync();
                    stopped.IsCompleted.ShouldBeFalse();
                }
                finally { release.TrySetResult(); }
                await AssertDelivery(restartedIncoming, restartedResponses, 99m);
                await stopped.WaitAsync(Deadline);
                workflow.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            }
            foreach (var port in ports)
            {
                port.Opens.ShouldBe(1);
                port.Closes.ShouldBe(1);
                port.Disposals.ShouldBe(1);
            }
            logs.Entries.Count.ShouldBe(0);
            received.Count.ShouldBe(0);

            async Task AssertDelivery(FlowOutputProbe incoming, FlowOutputProbe responses, decimal temperature)
            {
                var original = await incoming.Messages.ReceiveAsync(Deadline);
                var sent = await received.ReceiveAsync(Deadline);
                sent.Body.GetProperty("DeviceId").GetString().ShouldBe("serial-sensor");
                sent.Body.GetProperty("Temperature").GetDecimal().ShouldBe(temperature);
                sent.Body.GetProperty("Unit").GetString().ShouldBe("C");
                sent.Body.GetProperty("MessageId").GetString().ShouldBe(original.MessageId.Value);
                sent.Body.GetProperty("TraceId").GetString().ShouldBe(original.TraceId.Value);
                sent.MessageId.ShouldBe(original.MessageId.Value);
                var response = await responses.Messages.ReceiveAsync(Deadline);
                response.IsError.ShouldBeFalse(response.Error?.Message);
                response.Value.Json.GetProperty("StatusCode").GetInt32().ShouldBe(201);
                response.Value.Json.GetProperty("Body").GetString().ShouldBe("accepted");
                response.TraceId.ShouldBe(original.TraceId);
            }

            async Task AssertLoggedError(FlowOutputProbe incoming, FlowOutputProbe responses)
            {
                var original = await incoming.Messages.ReceiveAsync(Deadline);
                var error = await responses.Messages.ReceiveAsync(Deadline);
                error.IsError.ShouldBeTrue();
                error.TraceId.ShouldBe(original.TraceId);
                var entry = await logs.Entries.ReceiveAsync(Deadline);
                entry.Level.ShouldBe(LogLevel.Error);
                entry.Fields["Code"].ShouldBe(error.Error!.Code);
                entry.Fields["ErrorMessage"].ShouldBe(error.Error.Message);
                entry.Fields["TraceId"].ShouldBe(error.TraceId.Value);
                entry.Fields["MessageId"].ShouldBe(error.MessageId.Value);
            }
        }
        finally
        {
            await api.StopAsync().WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Http_command_sample_acknowledges_admission_logs_errors_and_drains_on_workflow_restart()
    {
        var ports = new List<TestPort>();
        var logs = new LogCapture("commands.log");
        await using var app = CreateCommandApp(ports, logs);
        await app.StartAsync().WaitAsync(Deadline);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline };
        var workflow = app.Services.GetRequiredKeyedService<FlowWorkflow>("commands");
        IFlowNode? previous = null;
        try
        {
            for (var run = 0; run < 2; run++)
            {
                if (run > 0) await workflow.StartAsync().WaitAsync(Deadline);
                ports.Count.ShouldBe(run + 1);
                var port = ports[run];
                var node = workflow.Services.GetRequiredKeyedService<IFlowNode>("commands.serial");
                node.ShouldNotBeSameAs(previous);
                using var incoming = app.Services.ObserveOutput(PortAddress.Parse("commands.receive.Output"));
                using var errors = app.Services.ObserveOutput(PortAddress.Parse("commands.serial.Output"));
                using var mapped = app.Services.ObserveOutput(PortAddress.Parse("commands.request.Output"));
                if (run == 0)
                {
                    await Post("{", HttpStatusCode.BadRequest);
                    foreach (var json in new[] { "{}", "{\"Text\":\"MOVE\\nSTOP\"}" })
                    {
                        await Post(json, HttpStatusCode.Accepted);
                        var original = await incoming.Messages.ReceiveAsync(Deadline);
                        (await mapped.Messages.ReceiveAsync(Deadline)).TraceId.ShouldBe(original.TraceId);
                        var error = await errors.Messages.ReceiveAsync(Deadline);
                        error.IsError.ShouldBeTrue();
                        error.TraceId.ShouldBe(original.TraceId);
                        var log = await logs.Entries.ReceiveAsync(Deadline);
                        log.Level.ShouldBe(LogLevel.Error);
                        log.Fields["Code"].ShouldBe(error.Error!.Code);
                        log.Fields["ErrorMessage"].ShouldBe(error.Error.Message);
                        log.Fields["TraceId"].ShouldBe(error.TraceId.Value);
                        port.Writes.ShouldBeEmpty();
                    }
                }

                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                port.BeforeWrite = _ => { entered.Set(); release.Wait(Deadline).ShouldBeTrue(); };
                try
                {
                    await Post("{\"Text\":\"MOVE 1\"}", HttpStatusCode.Accepted);
                    entered.Wait(Deadline).ShouldBeTrue();
                    port.Writes.ShouldBeEmpty(); // HTTP acknowledgement does not wait for serial execution.
                    var original = await incoming.Messages.ReceiveAsync(Deadline);
                    var command = await mapped.Messages.ReceiveAsync(Deadline);
                    command.Value.Json.GetProperty("Text").GetString().ShouldBe("MOVE 1");
                    command.TraceId.ShouldBe(original.TraceId);
                    await Post("{\"Text\":\"STOP\"}", HttpStatusCode.Accepted);
                    await incoming.Messages.ReceiveAsync(Deadline);
                    (await mapped.Messages.ReceiveAsync(Deadline)).Value.Json.GetProperty("Text").GetString().ShouldBe("STOP");

                    var stopped = workflow.StopAsync();
                    await Post("{\"Text\":\"late\"}", HttpStatusCode.ServiceUnavailable);
                    stopped.IsCompleted.ShouldBeFalse();
                    port.Closes.ShouldBe(0);
                }
                finally
                {
                    release.Set();
                    await workflow.StopAsync().WaitAsync(Deadline);
                }
                node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                port.Writes.ToArray().ShouldBe(new[] { "MOVE 1\n", "STOP\n" });
                port.Closes.ShouldBe(1);
                port.Disposals.ShouldBe(1);
                errors.Messages.TryReceive(out _).ShouldBeFalse();
                incoming.Messages.TryReceive(out _).ShouldBeFalse();
                previous = node;
            }
        }
        finally { await app.StopAsync().WaitAsync(Deadline); }
        logs.Entries.Count.ShouldBe(0);
        workflow.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var port in ports)
        {
            port.Opens.ShouldBe(1);
            port.Closes.ShouldBe(1);
            port.Disposals.ShouldBe(1);
        }

        async Task Post(string json, HttpStatusCode expected)
        {
            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/serial/commands", body);
            response.StatusCode.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("write-timeout")]
    public async Task Serial_io_failure_rejects_http_admission_drops_queued_writes_and_cleans_up(string operation)
    {
        var ports = new List<TestPort>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception failure = operation == "write-timeout"
            ? new TimeoutException("Simulated partial write timeout.")
            : new IOException("Simulated serial disconnect.");
        var attempts = 0;
        var app = CreateCommandApp(ports, new LogCapture("commands.log"));
        Exception? stopError = null, disposeError = null;
        try
        {
            await app.StartAsync().WaitAsync(Deadline);
            var workflow = app.Services.GetRequiredKeyedService<FlowWorkflow>("commands");
            var node = (SerialText)workflow.Services.GetRequiredKeyedService<IFlowNode>("commands.serial");
            var port = ports.Single();
            port.BeforeWrite = _ =>
            {
                Interlocked.Increment(ref attempts);
                entered.Set();
                release.Wait(Deadline).ShouldBeTrue();
                if (operation != "read") throw failure;
            };
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(() => stopping.TrySetResult());
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline };
            using var body = new StringContent("{\"Text\":\"MOVE 1\"}", Encoding.UTF8, "application/json");
            using var accepted = await client.PostAsync("/serial/commands", body);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            entered.Wait(Deadline).ShouldBeTrue();
            // Direct admission guarantees the second command is queued behind the blocked native write.
            (await node.Input.SendAsync(Request("queued")).WaitAsync(Deadline)).ShouldBeTrue();
            if (operation == "read")
            {
                port.Reads.Add(failure);
                AssertFailure(await Record.ExceptionAsync(() => node.Output.Completion.WaitAsync(Deadline)));
            }
            release.Set();

            AssertFailure(await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline)));
            AssertFailure(await Record.ExceptionAsync(() => workflow.Completion.WaitAsync(Deadline)));
            await stopping.Task.WaitAsync(Deadline);
            (await node.Input.SendAsync(Request("late")).WaitAsync(Deadline)).ShouldBeFalse();
            using var lateBody = new StringContent("{\"Text\":\"late\"}", Encoding.UTF8, "application/json");
            using var rejected = await client.PostAsync("/serial/commands", lateBody);
            rejected.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            attempts.ShouldBe(1);
            // A read failure cannot interrupt a native write already in progress.
            port.Writes.ToArray().ShouldBe(operation == "read" ? new[] { "MOVE 1\n" } : Array.Empty<string>());
        }
        finally
        {
            release.Set();
            stopError = await Record.ExceptionAsync(() => app.StopAsync().WaitAsync(Deadline));
            disposeError = await Record.ExceptionAsync(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
        }
        AssertFailure(stopError);
        AssertFailure(disposeError);
        attempts.ShouldBe(1);
        var disposedPort = ports.Single();
        disposedPort.Opens.ShouldBe(1);
        disposedPort.Closes.ShouldBe(1);
        disposedPort.Disposals.ShouldBe(1);

        void AssertFailure(Exception? error)
        {
            error.ShouldNotBeNull();
            if (error is AggregateException aggregate)
            {
                var errors = aggregate.Flatten().InnerExceptions;
                errors.ShouldNotBeEmpty();
                foreach (var inner in errors) inner.ShouldBeSameAs(failure);
            }
            else error.ShouldBeSameAs(failure);
        }
    }

    private static WebApplication CreateCommandApp(List<TestPort> ports, ILoggerProvider logs)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "HttpSample.json"));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpTriggers().AddMapper().AddLogger().AddJsonataExpressionEngine();
        AddSimulatedSerial(builder.Services, ports);
        builder.Services.AddFluxFlowApplication(builder.Configuration.GetRequiredSection("SerialCommands")).AddFluxFlow();
        var app = builder.Build();
        app.MapFluxFlowHttpTriggers();
        return app;
    }

    private static IHost CreateTelemetryHost(string apiAddress, List<TestPort> ports, ILoggerProvider logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "TelemetrySample.json"));
        builder.Configuration["SerialTelemetry:Resources:api:BaseAddress"] = apiAddress + "/";
        builder.Services.AddHttp().AddMapper().AddLogger().AddJsonataExpressionEngine();
        AddSimulatedSerial(builder.Services, ports);
        builder.Services.AddFluxFlowApplication(builder.Configuration.GetRequiredSection("SerialTelemetry")).AddFluxFlow();
        return builder.Build();
    }

    private static void AddSimulatedSerial(IServiceCollection registrations, List<TestPort> ports)
    {
        // Substitute only serial I/O. The sample's mapping, HTTP, logging and links use their normal registrars.
        registrations.AddKeyedSingleton<ResourceRegistration>("serial.port", (services, address, _) =>
            services.AddFluxFlowResource(address, _ =>
            {
                var port = new TestPort();
                ports.Add(port);
                return port;
            }));
        registrations.AddKeyedSingleton<ComponentRegistration>("serial.text", (services, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<SerialTextConfiguration>();
            var resource = ApplicationAddress.ResolveResourcePaths(config.Connection, address.WorkflowName)[0];
            services.AddComponentFactory(address, provider => provider.GetRequiredKeyedService<TestPort>(resource.Value).Create(new()
            {
                NewLine = config.NewLine, MaxLineBytes = config.MaxLineBytes,
                InputCapacity = config.InputCapacity, OutputCapacity = config.OutputCapacity
            }))
                .AddInputPort<SerialText>(address, "Input", node => node.Input)
                .AddOutputPort<SerialText>(address, "Output", node => node.Output)
                .AddEventPort<SerialText>(address, "Events", node => node.Events);
            services.AddLinks(address, config);
        });
    }

    private sealed class LogCapture(string category) : ILoggerProvider, ILogger
    {
        internal readonly BufferBlock<(LogLevel Level, Dictionary<string, object?> Fields)> Entries = new();
        public ILogger CreateLogger(string categoryName) => categoryName == category ? this : NullLogger.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Post((level, ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
        public void Dispose() { }
    }

    private static ComponentAddress RegisterSerial(IServiceCollection services, string workflow, Func<TestPort> create)
    {
        var resource = ApplicationAddress.WorkflowResource(workflow, "port");
        var address = ComponentAddress.Create(workflow, "serial");
        services.AddFluxFlowResource(resource, _ => create());
        services.AddComponentFactory(address, provider => provider.GetRequiredKeyedService<TestPort>(resource.Value).Create())
            .AddInputPort<SerialText>(address, "Input", node => node.Input)
            .AddOutputPort<SerialText>(address, "Output", node => node.Output)
            .AddEventPort<SerialText>(address, "Events", node => node.Events);
        return address;
    }

    private static FlowMessage Request(string text) => FlowMessage.Create(new JsonFlowValue(
        JsonSerializer.SerializeToElement(new SerialWriteRequest { Text = text }, SerialJsonContext.Default.SerialWriteRequest)));

    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    {
        var buffer = new BufferBlock<FlowMessage>();
        source.LinkTo(buffer, new() { PropagateCompletion = true });
        return buffer;
    }

    private sealed class TestPort : IDisposable
    {
        internal readonly BlockingCollection<object> Reads = new();
        internal readonly ConcurrentQueue<string> Writes = new();
        internal Exception? OpenError;
        internal Action<byte[]>? BeforeWrite;
        internal int Opens, Closes, Disposals;

        internal void ReadThroughIdleTimeout(byte[] bytes)
        {
            Reads.Add(bytes);
            // Dequeuing the timeout means the preceding bytes have been consumed by the parser.
            Reads.Add(new TimeoutException());
            SpinWait.SpinUntil(() => Reads.Count == 0, Deadline).ShouldBeTrue();
        }

        internal SerialText Create(SerialTextOptions? options = null) => new(options ?? new(),
            () => { if (OpenError is not null) throw OpenError; Interlocked.Increment(ref Opens); },
            buffer =>
            {
                if (!Reads.TryTake(out var item, 20)) throw new TimeoutException();
                if (item is Exception error) throw error;
                var bytes = (byte[])item;
                bytes.CopyTo(buffer, 0);
                return bytes.Length;
            },
            bytes => { BeforeWrite?.Invoke(bytes); Writes.Enqueue(Encoding.UTF8.GetString(bytes)); },
            () => Interlocked.Increment(ref Closes));

        public void Dispose() { Interlocked.Increment(ref Disposals); Reads.Dispose(); }
    }
}
