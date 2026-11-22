using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.DesignerApp.Services;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BluetoothRoundTripTests
{
    private const string Address = "010203040506";
    private static readonly Guid Service = new("3f63b38e-98df-4a52-a9f8-76b803a934b2");
    private static readonly Guid Characteristic = new("f50d7529-4e5d-4088-b88f-e236cd1e7f29");

    [Theory]
    [InlineData(BleSubscriptionMode.Notify, BleWriteMode.WithResponse)]
    [InlineData(BleSubscriptionMode.Indicate, BleWriteMode.WithoutResponse)]
    public async Task Client_server_and_notifications_share_bytes_and_isolate_stopped_runs(BleSubscriptionMode subscription, BleWriteMode write)
    {
        var link = new Loopback(subscription, write);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        builder.Services.AddSingleton<IBlePlatform>(link);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var http = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(http);
        var configuration = Configuration(subscription, write);
        var request = new EnvironmentRunRequest(configuration, new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var check = await runner.CheckAsync(request, token);
        check.Success.ShouldBeTrue(check.Message);
        Func<byte[], Task>? oldCallback = null;
        BleServerSession? oldSession = null;
        for (var run = 0; run < 2; run++)
        {
            var id = Guid.NewGuid();
            try
            {
                var status = await runner.StartAsync(id, request, token);
                status.State.ShouldBe("Running", status.Error);
                if (oldCallback is not null) await oldCallback([0xde, 0xad]);
                if (oldSession is not null)
                    await Should.ThrowAsync<OperationCanceledException>(() => oldSession.ReceiveWriteAsync(Service, Characteristic, [0xee]));
                oldCallback = link.Receiver.ShouldNotBeNull();
                oldSession = link.Server.ShouldNotBeNull();
                link.AllowTraffic.TrySetResult();

                while (Values(status, "subscription").Length == 0 || Values(status, "server").Length < 2)
                {
                    status.State.ShouldBe("Running", status.Error);
                    await Task.Delay(20, token);
                    status = await runner.GetAsync(id, token);
                }
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                link.Receiver.ShouldBeNull();
                link.Server.ShouldBeNull();
                Bytes(Values(stopped, "initial").Single()).ShouldBe(new byte[] { 1 });
                var receipt = Values(stopped, "writer").Single();
                receipt.GetProperty("Acknowledged").GetBoolean().ShouldBe(write == BleWriteMode.WithResponse);
                receipt.GetProperty("ByteLength").GetInt32().ShouldBe(3);
                Bytes(Values(stopped, "readback").Single()).ShouldBe(new byte[] { 0, 255, 66 });
                var server = Values(stopped, "server");
                server.Length.ShouldBe(2);
                foreach (var value in server)
                {
                    value.GetProperty("ServiceId").GetGuid().ShouldBe(Service);
                    value.GetProperty("CharacteristicId").GetGuid().ShouldBe(Characteristic);
                }
                Bytes(server.Single(v => v.GetProperty("Operation").GetString() == "Write")).ShouldBe(new byte[] { 0, 255, 66 });
                Bytes(server.Single(v => v.GetProperty("Operation").GetString() == "Updated")).ShouldBe(new byte[] { 254, 0, 128 });
                Bytes(Values(stopped, "subscription").Single()).ShouldBe(new byte[] { 254, 0, 128 });
                var retained = JsonSerializer.Serialize(stopped, EnvironmentJsonContext.Default.EnvironmentRunStatus);
                await oldCallback([0xde, 0xad]);
                JsonSerializer.Serialize(await runner.GetAsync(id, token), EnvironmentJsonContext.Default.EnvironmentRunStatus).ShouldBe(retained);
            }
            finally { link.AllowTraffic.TrySetResult(); await runner.StopAsync(id, CancellationToken.None); }
            link.AllowTraffic = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        request.Configuration.ShouldBe(configuration);
    }

    [Fact]
    public async Task Applied_write_with_late_confirmation_times_out_once_then_queued_read_returns_peer_bytes()
    {
        var settings = $$$"""
            {"Workflows":{"main":{
                "client":{"Type":"ble.client","DeviceAddress":"{{{Address}}}","UseInputOperation":true,"TimeoutSeconds":1},
                "peer":{"Type":"ble.server","ServiceId":"{{{Service}}}","Characteristics":[
                    {"Id":"{{{Characteristic}}}","Properties":"Read, Write","InitialValue":{"Encoding":"Hex","Data":"01"}}]}
            } } }
            """;
        using var config = WorkflowExecution.ReadConfiguration(settings);
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var applied = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new Loopback(BleSubscriptionMode.Notify, BleWriteMode.WithResponse)
        {
            ConfirmWrite = cancellation => { applied.TrySetResult(cancellation); return confirm.Task.WaitAsync(token); }
        };
        peer.AllowTraffic.SetResult();
        for (var run = 0; run < 2; run++)
        {
            var services = new ServiceCollection();
            WorkflowExecution.Register(services, config, plugins);
            services.AddSingleton<IBlePlatform>(peer);
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);
            var ports = provider.GetWorkflowServices("main");
            var input = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.client.Input");
            var outputs = new BufferBlock<FlowMessage>();
            var writes = new BufferBlock<FlowMessage>();
            var link = new DataflowLinkOptions { PropagateCompletion = true };
            using var clientLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.client.Output").LinkTo(outputs, link);
            using var peerLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.peer.Output").LinkTo(writes, link);
            var read = Command(new { Operation = "Read", ServiceId = Service, CharacteristicId = Characteristic });
            if (run == 0)
            {
                var write = Command(new { Operation = "Write", ServiceId = Service, CharacteristicId = Characteristic,
                    WriteMode = "WithResponse", Value = new { Encoding = "Hex", Data = "00FF" } });
                try
                {
                    (await input.SendAsync(write, token)).ShouldBeTrue();
                    var operation = await applied.Task.WaitAsync(token);
                    var stored = (await writes.ReceiveAsync(token)).Value.ShouldBeOfType<BleServerValue>();
                    stored.Operation.ShouldBe("Write");
                    Convert.FromBase64String(stored.ValueBase64).ShouldBe(new byte[] { 0, 255 });
                    (await input.SendAsync(read, token)).ShouldBeTrue();
                    var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = operation.Register(() => expired.TrySetResult());
                    await expired.Task.WaitAsync(token);
                    peer.Reads.ShouldBe(0);
                }
                finally { confirm.TrySetResult(); }
                var failure = await outputs.ReceiveAsync(token);
                failure.Value.ShouldBeOfType<FlowError>().Message.ShouldBe(new OperationCanceledException().Message);
                CheckLineage(failure, write);
            }
            else
            {
                peer.Reads.ShouldBe(1);
                (await input.SendAsync(read, token)).ShouldBeTrue();
            }

            var readback = await outputs.ReceiveAsync(token);
            CheckLineage(readback, read);
            Bytes(readback.Value.ShouldBeOfType<BleReadResult>().Json)
                .ShouldBe(run == 0 ? new byte[] { 0, 255 } : new byte[] { 1 });
            await app.StopAsync(token);
            peer.Reads.ShouldBe(run + 1);
            (await input.SendAsync(read, token)).ShouldBeFalse();
            (await outputs.OutputAvailableAsync(token)).ShouldBeFalse();
            (await writes.OutputAvailableAsync(token)).ShouldBeFalse();
        }

        static FlowMessage Command(object value) =>
            FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)), CorrelationId.New());

        static void CheckLineage(FlowMessage result, FlowMessage input)
        {
            result.TraceId.ShouldBe(input.TraceId);
            result.CorrelationId.ShouldBe(input.CorrelationId);
            result.CausationId.ShouldBe(input.MessageId);
        }
    }

    private static JsonElement[] Values(EnvironmentRunStatus status, string node) =>
        status.Outputs!.Records.Where(r => r.Port == $"main.{node}.Output")
            .Select(r => r.Message!.Value.GetProperty("Value")).ToArray();

    private static byte[] Bytes(JsonElement value)
    {
        var bytes = value.GetProperty("ValueBase64").GetBytesFromBase64();
        value.GetProperty("ByteLength").GetInt32().ShouldBe(bytes.Length);
        return bytes;
    }

    private static string Configuration(BleSubscriptionMode subscription, BleWriteMode write)
    {
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gatt-loopback.fluxflow")))!;
        var configuration = project["Configuration"]!;
        var workflow = configuration["Workflows"]!["main"]!;
        workflow["subscription"]!["SubscriptionMode"] = subscription.ToString();
        workflow["writer"]!["WriteMode"] = write.ToString();
        workflow["server"]!["Services"]![0]!["Characteristics"]![0]!["Properties"] =
            $"Read, {(write == BleWriteMode.WithResponse ? "Write" : "WriteWithoutResponse")}, {subscription}";
        return configuration.ToJsonString();
    }

    // Only the radio is substituted. Reads, writes and published updates go through the running server session.
    private sealed class Loopback(BleSubscriptionMode subscription, BleWriteMode write) : IBlePlatform
    {
        public TaskCompletionSource AllowTraffic = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<CancellationToken, Task>? ConfirmWrite { get; init; }
        public int Reads;
        public BleServerSession? Server { get; private set; }
        public Func<byte[], Task>? Receiver { get; private set; }

        public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation)
        {
            await AllowTraffic.Task.WaitAsync(cancellation);
            options.DeviceAddress.ShouldBe(Address);
            Interlocked.Increment(ref Reads);
            return Server!.Read(options.ServiceId, options.CharacteristicId);
        }

        public async Task WriteAsync(BleClientOptions options, byte[] bytes, BleWriteMode mode, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            options.DeviceAddress.ShouldBe(Address);
            mode.ShouldBe(write);
            await Server!.ReceiveWriteAsync(options.ServiceId, options.CharacteristicId, bytes);
            if (ConfirmWrite is not null) await ConfirmWrite(cancellation);
        }

        public async Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
        {
            options.DeviceAddress.ShouldBe(Address);
            options.ServiceId.ShouldBe(Service);
            options.CharacteristicId.ShouldBe(Characteristic);
            options.SubscriptionMode.ShouldBe(subscription);
            Receiver = received;
            started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Receiver = null; }
        }

        public async Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation)
        {
            Server = session;
            session.SetNotifier((service, characteristic, bytes) =>
            {
                service.ShouldBe(Service);
                characteristic.ShouldBe(Characteristic);
                return Receiver.ShouldNotBeNull()(bytes);
            });
            started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Server = null; }
        }

        public Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation) =>
            throw new NotSupportedException("This trial requires the workflow-backed server session.");
    }
}
