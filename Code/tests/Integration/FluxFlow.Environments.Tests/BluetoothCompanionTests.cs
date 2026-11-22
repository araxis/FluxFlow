using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BluetoothCompanionTests
{
    private const string Configuration = """
        {"Workflows":{"main":{
          "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"read.Input"},
          "read":{"Type":"ble.client","DeviceAddress":"01:02:03:04:05:06",
            "ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2","CharacteristicId":"f50d7529-4e5d-4088-b88f-e236cd1e7f29"},
          "serve":{"Type":"ble.server","ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2",
            "Characteristics":[{"Id":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Value":"hello"}]}
        }}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Companion_uses_registered_backend_and_retains_results_after_stop(bool available)
    {
        var platform = new Platform();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        if (available) builder.Services.AddSingleton<IBlePlatform>(platform);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest(Configuration, new());
        var original = JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var check = await runner.CheckAsync(request, deadline.Token);
        platform.Reads.ShouldBe(0);
        platform.Starts.ShouldBe(0);
        check.Success.ShouldBe(available);
        if (!available)
        {
            check.Message.ShouldContain("main.read");
            check.Message.ShouldContain("main.serve");
            check.Message.ShouldContain("Windows BLE-enabled companion");
            return;
        }

        for (var index = 0; index < 2; index++)
        {
            var id = Guid.NewGuid();
            try
            {
                var status = await runner.StartAsync(id, request, deadline.Token);
                status.State.ShouldBe("Running", status.Error);
                while (status.Outputs!.Records.Count(record => record.Port == "main.read.Output") < 2)
                {
                    await Task.Delay(25, deadline.Token);
                    status = await runner.GetAsync(id, deadline.Token);
                    status.State.ShouldBe("Running", status.Error);
                }
                JsonSerializer.Serialize(status.Outputs).ShouldContain("aGVsbG8=");
                await runner.StopAsync(id, deadline.Token);
                var stopped = await runner.GetAsync(id, deadline.Token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var retained = JsonSerializer.Serialize(stopped, EnvironmentJsonContext.Default.EnvironmentRunStatus);
                await Task.Delay(100, deadline.Token);
                JsonSerializer.Serialize(await runner.GetAsync(id, deadline.Token), EnvironmentJsonContext.Default.EnvironmentRunStatus).ShouldBe(retained);
                platform.Stops.ShouldBe(index + 1);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        platform.Starts.ShouldBe(2);
        JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
    }

    [Theory]
    [InlineData(BleSubscriptionMode.Notify)]
    [InlineData(BleSubscriptionMode.Indicate)]
    public async Task Dynamic_operations_and_notifications_deliver_through_companion_and_remain_isolated_after_stop(BleSubscriptionMode mode)
    {
        var config = JsonNode.Parse(Configuration)!;
        var workflow = config["Workflows"]!["main"]!.AsObject();
        workflow["tick"]!["Output"] = "operations.Input"; workflow.Remove("serve");
        var read = workflow["read"]!;
        var subscription = read.DeepClone();
        subscription["Type"] = "ble.notifications";
        subscription["SubscriptionMode"] = mode.ToString();
        workflow["notifications"] = subscription;
        read["UseInputOperation"] = true;
        read["WriteMode"] = 42; // Retained fixed-mode settings do not override message commands.
        workflow["operations"] = new JsonObject
        {
            ["Type"] = "source", ["Output"] = "read.Input",
            ["Values"] = """
                [{"Operation":"Write","ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2","CharacteristicId":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Value":{"Encoding":"Hex","Data":"00FF42"}},
                 {"Operation":"Read","ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2","CharacteristicId":"f50d7529-4e5d-4088-b88f-e236cd1e7f29"}]
                """
        };
        var platform = new Platform();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        builder.Services.AddLocalFlowRunner(plugins); builder.Services.AddSingleton<IBlePlatform>(platform);
        await using var app = builder.Build(); app.MapLocalFlowRunner(); await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest(config.ToJsonString(), new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        (await runner.CheckAsync(request, deadline.Token)).Success.ShouldBeTrue();
        platform.Writes.ShouldBe(0); platform.Subscriptions.ShouldBe(0);
        Func<byte[], Task>? previousCallback = null;
        for (var index = 0; index < 2; index++)
        {
            var id = Guid.NewGuid();
            var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            platform.ReadRelease = releaseRead.Task;
            try
            {
                var status = await runner.StartAsync(id, request, deadline.Token);
                while (Volatile.Read(ref platform.Reads) < index + 1 ||
                       status.Outputs!.Records.Count(record => record.Port == "main.notifications.Output") < 3)
                {
                    status.State.ShouldBe("Running", status.Error);
                    await Task.Delay(25, deadline.Token);
                    status = await runner.GetAsync(id, deadline.Token);
                }
                platform.SubscriptionMode.ShouldBe(mode);
                if (previousCallback is not null) await previousCallback([99]);
                var stopping = runner.StopAsync(id, deadline.Token);
                while ((status = await runner.GetAsync(id, deadline.Token)).State == "Running")
                    await Task.Delay(10, deadline.Token);
                status.State.ShouldBe("Stopping", status.Error);
                stopping.IsCompleted.ShouldBeFalse();
                releaseRead.SetResult();
                await stopping;
                var stopped = await runner.GetAsync(id, deadline.Token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var clientOutput = JsonSerializer.Serialize(stopped.Outputs!.Records.Where(record => record.Port == "main.read.Output"));
                clientOutput.ShouldContain("Acknowledged"); clientOutput.ShouldContain("aGVsbG8=");
                var notifications = JsonSerializer.Serialize(stopped.Outputs.Records.Where(record => record.Port == "main.notifications.Output"));
                notifications.ShouldContain("AP9C");
                notifications.ShouldNotContain("Yw=="); // Neither the previous run nor unsubscribe may emit [99].
                platform.SubscriptionStops.ShouldBe(index + 1);
                platform.Writes.ShouldBe(index + 1);
                platform.Written.ShouldBe(new byte[] { 0, 255, 66 });
                var retained = JsonSerializer.Serialize(stopped, EnvironmentJsonContext.Default.EnvironmentRunStatus);
                previousCallback = platform.Callback!;
                await previousCallback([99]);
                await Task.Delay(50, deadline.Token);
                JsonSerializer.Serialize(await runner.GetAsync(id, deadline.Token), EnvironmentJsonContext.Default.EnvironmentRunStatus).ShouldBe(retained);
            }
            finally { releaseRead.TrySetResult(); await runner.StopAsync(id, CancellationToken.None); }
        }
        request.Configuration.ShouldBe(config.ToJsonString());
    }

    [Fact]
    public async Task Peripheral_bindings_and_capability_checks_are_read_only_and_run_local()
    {
        var config = JsonNode.Parse(Configuration)!;
        config["Workflows"]!["main"]!.AsObject().Remove("serve");
        config["Workflows"]!["main"]!["read"]!["Connection"] = "sensor";
        config["Resources"] = JsonNode.Parse("""{"sensor":{"Type":"ble.peripheral","Endpoint":"ble://010203040506/public"}}""");
        var platform = new Platform();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        builder.Services.AddLocalFlowRunner(plugins); builder.Services.AddSingleton<IBlePlatform>(platform);
        await using var app = builder.Build(); app.MapLocalFlowRunner(); await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest(config.ToJsonString(), new()) { Instances = [new("first", "First")
        { ExistingServices = new() { ["Resources:sensor"] = new() { Type = "ble.peripheral", Endpoint = "ble://112233445566/random" } } }] };
        var original = JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        platform.CapabilityFailure = "Bluetooth is off.";
        var check = await runner.CheckAsync(request, deadline.Token);
        check.Success.ShouldBeFalse(); check.Message.ShouldContain("main.read"); platform.Reads.ShouldBe(0);
        platform.CapabilityFailure = null;
        (await runner.CheckAsync(request, deadline.Token)).Success.ShouldBeTrue();
        var id = Guid.NewGuid();
        try
        {
            var status = await runner.StartAsync(id, request, deadline.Token);
            status.State.ShouldBe("Running", status.Error);
            while (platform.Reads < 2) await Task.Delay(25, deadline.Token);
            platform.LastRead!.DeviceAddress.ShouldBe("112233445566");
            platform.LastRead.AddressType.ShouldBe(BleAddressType.Random);
            await runner.StopAsync(id, deadline.Token);
            (await runner.GetAsync(id, deadline.Token)).State.ShouldBe("Stopped");
            JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        }
        finally { await runner.StopAsync(id, CancellationToken.None); }
    }

    [Fact]
    public async Task Multi_service_values_accept_workflow_updates_and_emit_writes_with_stop_and_rerun_isolation()
    {
        const string service = "3f63b38e-98df-4a52-a9f8-76b803a934b2";
        const string characteristic = "f50d7529-4e5d-4088-b88f-e236cd1e7f29";
        var config = """
        {"Workflows":{"main":{
          "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"updates.Input"},
          "updates":{"Type":"source","Values":"[{\"ServiceId\":\"$service\",\"CharacteristicId\":\"$characteristic\",\"Value\":{\"Encoding\":\"Hex\",\"Data\":\"00FF42\"},\"Notify\":true}]","Output":"serve.Input"},
          "serve":{"Type":"ble.server","Services":[
            {"Id":"$service","Characteristics":[{"Id":"$characteristic","Properties":"Read, Write, Notify","InitialValue":{"Encoding":"Hex","Data":"01"}}]},
            {"Id":"0000180f-0000-1000-8000-00805f9b34fb","Characteristics":[{"Id":"00002a19-0000-1000-8000-00805f9b34fb","InitialValue":{"Encoding":"Hex","Data":"64"}}]}]}
        }}}
        """.Replace("$service", service).Replace("$characteristic", characteristic);
        var platform = new Platform { InteractiveServer = true };
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        builder.Services.AddLocalFlowRunner(plugins); builder.Services.AddSingleton<IBlePlatform>(platform);
        await using var app = builder.Build(); app.MapLocalFlowRunner(); await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest(config, new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        (await runner.CheckAsync(request, deadline.Token)).Success.ShouldBeTrue();
        BleServerSession? previous = null;
        for (var run = 0; run < 2; run++)
        {
            var id = Guid.NewGuid();
            try
            {
                var status = await runner.StartAsync(id, request, deadline.Token);
                while (!JsonSerializer.Serialize(status.Outputs).Contains("Updated", StringComparison.Ordinal))
                {
                    status.State.ShouldBe("Running", status.Error);
                    await Task.Delay(25, deadline.Token); status = await runner.GetAsync(id, deadline.Token);
                }
                var session = platform.Session!;
                if (previous is not null)
                {
                    Should.Throw<OperationCanceledException>(() => previous.Read(Guid.Parse(service), Guid.Parse(characteristic)));
                    await Should.ThrowAsync<OperationCanceledException>(() => previous.ReceiveWriteAsync(Guid.Parse(service), Guid.Parse(characteristic), [88]));
                }
                session.Read(Guid.Parse(service), Guid.Parse(characteristic)).ShouldBe(new byte[] { 0, 255, 66 });
                previous = session;
                await session.ReceiveWriteAsync(Guid.Parse(service), Guid.Parse(characteristic), [99]);
                do { await Task.Delay(25, deadline.Token); status = await runner.GetAsync(id, deadline.Token); }
                while (!JsonSerializer.Serialize(status.Outputs).Contains("Write", StringComparison.Ordinal));
                platform.InitialValue.ShouldBe(new byte[] { 1 });
                platform.Notified.ShouldBe(new byte[] { 0, 255, 66 });
                await runner.StopAsync(id, deadline.Token);
                status = await runner.GetAsync(id, deadline.Token);
                status.State.ShouldBe("Stopped", status.Error);
                var retained = JsonSerializer.Serialize(status, EnvironmentJsonContext.Default.EnvironmentRunStatus);
                await Should.ThrowAsync<OperationCanceledException>(() => session.ReceiveWriteAsync(Guid.Parse(service), Guid.Parse(characteristic), [88]));
                JsonSerializer.Serialize(await runner.GetAsync(id, deadline.Token), EnvironmentJsonContext.Default.EnvironmentRunStatus).ShouldBe(retained);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        platform.Stops.ShouldBe(2); request.Configuration.ShouldBe(config);
    }

    [Fact]
    public async Task Peer_write_during_notification_keeps_its_value_and_both_output_identities_across_runs()
    {
        var service = Guid.Parse("3f63b38e-98df-4a52-a9f8-76b803a934b2");
        var characteristic = Guid.Parse("f50d7529-4e5d-4088-b88f-e236cd1e7f29");
        var json = """
            {"Workflows":{"main":{"server":{"Type":"ble.server","Services":[
              {"Id":"SERVICE","Characteristics":[{"Id":"CHARACTERISTIC","Properties":"Read, Write, Notify",
                "InitialValue":{"Encoding":"Hex","Data":"01"}}]}]}}}}
            """.Replace("SERVICE", service.ToString()).Replace("CHARACTERISTIC", characteristic.ToString());
        using var config = WorkflowExecution.ReadConfiguration(json);
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var platform = new Platform { InteractiveServer = true };
        BleServerSession? previous = null;
        for (var run = 0; run < 2; run++)
        {
            var services = new ServiceCollection();
            WorkflowExecution.Register(services, config, plugins);
            services.AddSingleton<IBlePlatform>(platform);
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);
            var session = platform.Session!;
            var ports = provider.GetWorkflowServices("main");
            var input = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.server.Input");
            var outputs = new BufferBlock<FlowMessage>();
            using var link = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.server.Output")
                .LinkTo(outputs, new DataflowLinkOptions { PropagateCompletion = true });
            if (previous is not null)
                await Should.ThrowAsync<OperationCanceledException>(() => previous.ReceiveWriteAsync(service, characteristic, [88]));
            session.Read(service, characteristic).ShouldBe(new byte[] { 1 });

            var entered = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var notifications = 0;
            session.SetNotifier((serviceId, characteristicId, value) =>
            {
                serviceId.ShouldBe(service);
                characteristicId.ShouldBe(characteristic);
                Interlocked.Increment(ref notifications);
                entered.TrySetResult(value.ToArray());
                return release.Task.WaitAsync(token);
            });
            var update = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new
            {
                ServiceId = service, CharacteristicId = characteristic, Value = new { Encoding = "Hex", Data = "00FF" }, Notify = true
            })), CorrelationId.New());
            try
            {
                (await input.SendAsync(update, token)).ShouldBeTrue();
                (await entered.Task.WaitAsync(token)).ShouldBe(new byte[] { 0, 255 });
                session.Read(service, characteristic).ShouldBe(new byte[] { 0, 255 });
                await session.ReceiveWriteAsync(service, characteristic, [128, 1]).WaitAsync(token);
                var written = await outputs.ReceiveAsync(token);
                CheckReceipt(written, "Write", [128, 1]);
                written.TraceId.ShouldNotBe(update.TraceId);
                written.CorrelationId.ShouldBeNull();
                written.CausationId.ShouldBeNull();
            }
            finally { release.TrySetResult(); }

            var updated = await outputs.ReceiveAsync(token);
            CheckReceipt(updated, "Updated", [0, 255]);
            updated.TraceId.ShouldBe(update.TraceId);
            updated.CorrelationId.ShouldBe(update.CorrelationId);
            updated.CausationId.ShouldBe(update.MessageId);
            session.Read(service, characteristic).ShouldBe(new byte[] { 128, 1 });
            await app.StopAsync(token);
            await Should.ThrowAsync<OperationCanceledException>(() => session.ReceiveWriteAsync(service, characteristic, [88]));
            (await outputs.OutputAvailableAsync(token)).ShouldBeFalse();
            notifications.ShouldBe(1);
            previous = session;
        }
        platform.Stops.ShouldBe(2);

        void CheckReceipt(FlowMessage message, string operation, byte[] bytes)
        {
            var receipt = message.Value.ShouldBeOfType<BleServerValue>();
            receipt.Operation.ShouldBe(operation);
            receipt.ServiceId.ShouldBe(service);
            receipt.CharacteristicId.ShouldBe(characteristic);
            receipt.ByteLength.ShouldBe(bytes.Length);
            Convert.FromBase64String(receipt.ValueBase64).ShouldBe(bytes);
        }
    }

    private sealed class Platform : IBlePlatform, IBleCapabilities
    {
        public int Reads, Starts, Stops;
        public int Writes, Subscriptions, SubscriptionStops;
        public BleSubscriptionMode? SubscriptionMode;
        public byte[]? Written;
        public Func<byte[], Task>? Callback;
        public BleClientOptions? LastRead;
        public Task ReadRelease = Task.CompletedTask;
        public string? CapabilityFailure;
        public bool InteractiveServer;
        public BleServerSession? Session;
        public byte[]? InitialValue, Notified;
        public Task CheckAsync(bool peripheral, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return CapabilityFailure is null ? Task.CompletedTask : Task.FromException(new InvalidOperationException(CapabilityFailure)); }
        public Task WriteAsync(BleClientOptions options, byte[] value, BleWriteMode mode, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); options.Validate(); Written = value.ToArray(); Interlocked.Increment(ref Writes); return Task.CompletedTask; }
        public async Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
        {
            options.ValidateSubscription(); Callback = received; SubscriptionMode = options.SubscriptionMode;
            Interlocked.Increment(ref Subscriptions); started();
            try { while (true) { await received([0, 255, 66]); await Task.Delay(15, cancellation); } }
            finally { await received([99]); Interlocked.Increment(ref SubscriptionStops); }
        }
        public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            options.Validate();
            LastRead = options;
            Interlocked.Increment(ref Reads);
            await ReadRelease;
            return "hello"u8.ToArray();
        }
        public async Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation)
        {
            if (!InteractiveServer) { await ServeAsync(options, started, cancellation); return; }
            options.Validate(); Session = session; Interlocked.Increment(ref Starts);
            var service = options.GetServices()[0];
            InitialValue = session.Read(service.Id, service.Characteristics[0].Id);
            session.SetNotifier((_, _, value) => { Notified = value.ToArray(); return Task.CompletedTask; });
            started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { session.Close(); Interlocked.Increment(ref Stops); }
        }
        public async Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation)
        {
            options.Validate();
            Interlocked.Increment(ref Starts);
            started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Interlocked.Increment(ref Stops); }
        }
    }
}
