using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Bluetooth.Tests;

public sealed class BleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);
    private static BleClientOptions ClientOptions => new() { DeviceAddress = "01:02:03:04:05:06", ServiceId = Guid.NewGuid(), CharacteristicId = Guid.NewGuid() };
    private static BleServerOptions ServerOptions => new() { ServiceId = Guid.NewGuid(), Characteristics = [new() { Id = Guid.NewGuid(), Value = "hello" }] };

    [Fact]
    public async Task Client_reads_repeatedly_and_preserves_message_lineage_and_original_bytes()
    {
        var platform = new TestPlatform();
        await using var node = new BleClient(platform, ClientOptions);
        var output = new BufferBlock<FlowMessage>();
        node.Output.LinkTo(output);
        for (var index = 0; index < 2; index++)
        {
            var input = FlowMessage.Create(JsonFlowValue.Null);
            (await node.Input.SendAsync(input)).ShouldBeTrue();
            var result = await output.ReceiveAsync(Deadline);
            result.CausationId.ShouldBe(input.MessageId);
            result.TraceId.ShouldBe(input.TraceId);
            result.Value.ShouldBeOfType<BleReadResult>().ValueBase64.ShouldBe("AP8B");
            result.Value.Json.GetProperty("ByteLength").GetInt32().ShouldBe(3);
        }
        node.Input.Complete();
        await node.Completion.WaitAsync(Deadline);
        platform.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task Client_rejects_a_native_result_returned_after_the_read_deadline()
    {
        await using var node = new BleClient(new LateReadPlatform(), ClientOptions with { TimeoutSeconds = 1 });
        var output = new BufferBlock<FlowMessage>();
        node.Output.LinkTo(output);
        (await node.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null))).ShouldBeTrue();
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
    }

    private sealed class LateReadPlatform : IBlePlatform
    {
        public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation)
        {
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            return [42];
        }
        public Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Cancelling_server_start_waits_for_cleanup_and_rejects_a_late_started_callback()
    {
        var platform = new PendingServerPlatform();
        await using var server = new BleGattServer(platform, ServerOptions);
        var events = new BufferBlock<FlowMessage>();
        server.Events.LinkTo(events);
        using var cancel = new CancellationTokenSource();
        var starting = server.StartAsync(cancel.Token);
        await platform.Entered.Task.WaitAsync(Deadline);
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => starting.WaitAsync(Deadline));
        await server.Completion.WaitAsync(Deadline);
        platform.Stops.ShouldBe(1);
        events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Server_uses_a_snapshot_and_keeps_advertising_defaults_for_existing_configuration()
    {
        var options = ServerOptions;
        var platform = new PendingServerPlatform();
        await using var server = new BleGattServer(platform, options);
        options.Characteristics[0] = new() { Id = Guid.NewGuid(), Value = "changed" };
        using var cancel = new CancellationTokenSource();
        var starting = server.StartAsync(cancel.Token);
        await platform.Entered.Task.WaitAsync(Deadline);
        platform.Options!.Characteristics[0].Value.ShouldBe("hello");
        platform.Options.IsConnectable.ShouldBeTrue();
        platform.Options.IsDiscoverable.ShouldBeTrue();
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => starting.WaitAsync(Deadline));
    }

    private sealed class PendingServerPlatform : IBlePlatform
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BleServerOptions? Options;
        public int Stops;
        public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation) => ServeAsync(options, started, cancellation);
        public async Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation)
        {
            Options = options;
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { started(); Interlocked.Increment(ref Stops); }
        }
    }

    [Fact]
    public async Task Server_waits_for_start_and_cleans_up_when_production_stops()
    {
        var platform = new TestPlatform();
        await using var server = new BleGattServer(platform, ServerOptions);
        await server.StartAsync().WaitAsync(Deadline);
        platform.Servers.ShouldBe(1);
        server.StopProduction();
        await server.Completion.WaitAsync(Deadline);
        platform.Stops.ShouldBe(1);
    }

    [Fact]
    public async Task Server_keeps_unconfirmed_cleanup_as_a_failure()
    {
        var platform = new TestPlatform { FailCleanup = true };
        var server = new BleGattServer(platform, ServerOptions);
        await server.StartAsync().WaitAsync(Deadline);
        server.StopProduction();
        var error = await Should.ThrowAsync<Exception>(() => server.Completion.WaitAsync(Deadline));
        error.ToString().ShouldContain("Unconfirmed advertising Stop");
        try { await server.DisposeAsync(); } catch (Exception) { }
        platform.Stops.ShouldBe(1);
    }

    [Fact]
    public async Task Missing_platform_fails_without_claiming_server_started()
    {
        var server = new BleGattServer(new UnavailableBlePlatform(), ServerOptions);
        await Should.ThrowAsync<PlatformNotSupportedException>(() => server.StartAsync().WaitAsync(Deadline));
        try { await server.DisposeAsync(); } catch (Exception) { }
    }

    [Fact]
    public void Configuration_rejects_invalid_addresses_duplicate_characteristics_and_large_values()
    {
        Should.Throw<ArgumentException>(() => (ClientOptions with { DeviceAddress = "nearby" }).Validate());
        Should.Throw<ArgumentException>(() => (ClientOptions with { DeviceAddress = " 12345678901" }).Validate());
        Should.Throw<ArgumentException>(() => (ClientOptions with { ServiceId = Guid.Empty }).Validate());
        var options = ServerOptions;
        Should.Throw<ArgumentException>(() => (options with { Characteristics = [options.Characteristics[0], options.Characteristics[0]] }).Validate());
        Should.Throw<ArgumentException>(() => (options with { Characteristics = [new() { Id = Guid.NewGuid(), Value = new string('x', 513) }] }).Validate());
    }

    private sealed class TestPlatform : IBlePlatform
    {
        public int Reads, Servers, Stops;
        public bool FailCleanup;
        public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<byte[]>([0, 255, 1]); }
        public Task ServeAsync(BleServerOptions options, BleServerSession session, Action started, CancellationToken cancellation) => ServeAsync(options, started, cancellation);
        public async Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Servers++;
            started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Stops++; if (FailCleanup) throw new TimeoutException("Unconfirmed advertising Stop"); }
        }
    }
}
