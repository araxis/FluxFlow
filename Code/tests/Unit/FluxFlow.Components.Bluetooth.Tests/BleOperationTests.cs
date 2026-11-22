using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Bluetooth.Tests;

public sealed class BleOperationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);
    private static BleClientOptions Options => new() { DeviceAddress = "010203040506", ServiceId = Guid.NewGuid(), CharacteristicId = Guid.NewGuid() };
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    [Theory]
    [InlineData("Hex", "00FF42")]
    [InlineData("Base64", "AP9C")]
    public async Task Dynamic_read_write_use_message_targets_and_preserve_payload_lineage(string encoding, string data)
    {
        var backend = new Platform();
        var options = Options with { UseInputOperation = true, ServiceId = Guid.Empty, CharacteristicId = Guid.Empty,
            WriteMode = (BleWriteMode)42 }; // A retained fixed setting is not part of the incoming command.
        await using var client = new BleClient(backend, options);
        var output = new BufferBlock<FlowMessage>(); client.Output.LinkTo(output);
        var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
        var input = Message(new { operation = "write", serviceId = service, characteristicId = characteristic,
            writeMode = "WithoutResponse", value = new { encoding, data } });
        await client.Input.SendAsync(input);
        var written = await output.ReceiveAsync(Deadline);
        written.IsError.ShouldBeFalse();
        written.CausationId.ShouldBe(input.MessageId); written.TraceId.ShouldBe(input.TraceId);
        written.Value.ShouldBeOfType<BleWriteResult>().Acknowledged.ShouldBeFalse();
        backend.Bytes.ShouldBe(new byte[] { 0, 255, 66 });
        backend.Target!.ServiceId.ShouldBe(service); backend.Target.CharacteristicId.ShouldBe(characteristic);
        backend.Target.Operation.ShouldBe(BleOperationKind.Write);
        backend.Target.WriteMode.ShouldBe(BleWriteMode.WithoutResponse);
        await client.Input.SendAsync(Message(new { Operation = "Read", ServiceId = service, CharacteristicId = characteristic }));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<BleReadResult>().ValueBase64.ShouldBe("AP9C");
        client.Input.Complete(); await client.Completion.WaitAsync(Deadline);
        backend.Writes.ShouldBe(1); backend.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_dynamic_operation_does_not_access_device_and_next_message_still_works()
    {
        var backend = new Platform(); var options = Options;
        await using var client = new BleClient(backend, options with { UseInputOperation = true });
        var output = new BufferBlock<FlowMessage>(); client.Output.LinkTo(output);
        foreach (var operation in new object[] {
            new { ServiceId = options.ServiceId, CharacteristicId = options.CharacteristicId },
            new { Operation = "Delete", ServiceId = options.ServiceId, CharacteristicId = options.CharacteristicId },
            new { Operation = "Write", ServiceId = options.ServiceId, CharacteristicId = options.CharacteristicId, Value = new { Encoding = "Hex", Data = "ZZ" } } })
        {
            await client.Input.SendAsync(Message(operation));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        }
        backend.Reads.ShouldBe(0); backend.Writes.ShouldBe(0);
        await client.Input.SendAsync(Message(new { Operation = "Read", options.ServiceId, options.CharacteristicId }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        client.Input.Complete(); await client.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Late_write_result_is_rejected_without_retry_and_cleanup_is_awaited()
    {
        var backend = new Platform { LateWrite = true };
        await using var client = new BleClient(backend, Options with { Operation = BleOperationKind.Write,
            Value = new() { Encoding = BleValueEncoding.Utf8, Data = "hello" }, TimeoutSeconds = 1 });
        var output = new BufferBlock<FlowMessage>(); client.Output.LinkTo(output);
        await client.Input.SendAsync(Message(new { }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        backend.Writes.ShouldBe(1); backend.Cleaned.ShouldBeTrue();
        client.Input.Complete(); await client.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(BleOperationKind.Read)]
    [InlineData(BleOperationKind.Write)]
    public async Task Disconnect_errors_belong_to_the_failed_input_and_are_not_retried(BleOperationKind operation)
    {
        var disconnect = new IOException("Device disconnected during GATT operation.");
        var attempts = 0;
        var backend = new Platform { BeforeReply = _ =>
            ++attempts == 1 ? Task.FromException(disconnect) : Task.CompletedTask };
        await using var client = new BleClient(backend, Options with { Operation = operation, Value = new() { Data = "FF" } });
        var output = new BufferBlock<FlowMessage>(); client.Output.LinkTo(output);
        var first = Message(1); var second = Message(2);
        await client.Input.SendAsync(first);
        await client.Input.SendAsync(second);
        client.Complete();
        var failed = await output.ReceiveAsync(Deadline);
        failed.Value.ShouldBeOfType<FlowError>().Message.ShouldBe(disconnect.Message);
        failed.CausationId.ShouldBe(first.MessageId);
        var recovered = await output.ReceiveAsync(Deadline);
        recovered.IsError.ShouldBeFalse(); recovered.CausationId.ShouldBe(second.MessageId);
        if (operation == BleOperationKind.Write) recovered.Value.ShouldBeOfType<BleWriteResult>().Acknowledged.ShouldBeTrue();
        await client.Completion.WaitAsync(Deadline);
        attempts.ShouldBe(2);
        output.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(BleOperationKind.Read)]
    [InlineData(BleOperationKind.Write)]
    public async Task Aborted_run_awaits_late_device_reply_without_publishing_or_dispatching_queued_input(BleOperationKind operation)
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Platform { BeforeReply = token => { entered.SetResult(token); return release.Task; } };
        var options = Options with { Operation = operation, Value = new() { Data = "00FF" } };
        await using var first = new BleClient(backend, options);
        var oldOutput = new BufferBlock<FlowMessage>(); first.Output.LinkTo(oldOutput);
        try
        {
            await first.Input.SendAsync(Message(1));
            var operationToken = await entered.Task.WaitAsync(Deadline);
            (await first.Input.SendAsync(Message(2))).ShouldBeTrue();
            first.Fault(new IOException("Run aborted."));
            operationToken.IsCancellationRequested.ShouldBeTrue();
            first.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        (await Should.ThrowAsync<IOException>(() => first.Completion.WaitAsync(Deadline))).Message.ShouldBe("Run aborted.");
        (backend.Reads + backend.Writes).ShouldBe(1);
        backend.BeforeReply = null;
        await using var next = new BleClient(backend, options);
        var output = new BufferBlock<FlowMessage>(); next.Output.LinkTo(output);
        var fresh = Message(3);
        await next.Input.SendAsync(fresh); next.Complete();
        var result = await output.ReceiveAsync(Deadline);
        result.CausationId.ShouldBe(fresh.MessageId); result.IsError.ShouldBeFalse();
        await next.Completion.WaitAsync(Deadline);
        (backend.Reads + backend.Writes).ShouldBe(2);
        oldOutput.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Notifications_deliver_original_bytes_repeatedly_and_reject_late_callbacks_after_stop()
    {
        var backend = new Platform();
        await using var source = new BleNotifications(backend, Options with { OutputCapacity = 1 });
        var output = new BufferBlock<FlowMessage>(); source.Output.LinkTo(output);
        await source.StartAsync().WaitAsync(Deadline);
        var received = backend.Received!;
        await received([0, 255, 66]);
        var first = await output.ReceiveAsync(Deadline);
        first.Value.ShouldBeOfType<BleReadResult>().ValueBase64.ShouldBe("AP9C");
        for (byte index = 0; index < 100; index++)
        {
            await received([index]);
            (await output.ReceiveAsync(Deadline)).Value.ShouldBeOfType<BleReadResult>().ValueBase64.ShouldBe(Convert.ToBase64String([index]));
        }
        source.StopProduction();
        await source.ProductionCompletion.WaitAsync(Deadline);
        backend.Cleaned.ShouldBeTrue();
        await received([99]);
        await source.Completion.WaitAsync(Deadline);
        output.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelling_subscription_start_awaits_cleanup_and_ignores_late_ready_and_values()
    {
        var backend = new Platform { DelayStartup = true };
        await using var source = new BleNotifications(backend, Options);
        using var cancel = new CancellationTokenSource();
        var starting = source.StartAsync(cancel.Token);
        await backend.Entered.Task.WaitAsync(Deadline);
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => starting.WaitAsync(Deadline));
        await source.Completion.WaitAsync(Deadline);
        backend.Cleaned.ShouldBeTrue();
        source.Output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Unsubscribe_failure_remains_visible_after_stop()
    {
        var backend = new Platform { FailCleanup = true };
        var source = new BleNotifications(backend, Options);
        await source.StartAsync().WaitAsync(Deadline);
        source.StopProduction();
        var error = await Should.ThrowAsync<Exception>(() => source.Completion.WaitAsync(Deadline));
        error.ToString().ShouldContain("Unsubscribe failed");
        try { await source.DisposeAsync(); } catch (Exception) { }
        backend.Cleaned.ShouldBeTrue();
    }

    private sealed class Platform : IBlePlatform
    {
        public int Reads, Writes;
        public byte[] Bytes = [0, 255, 66];
        public BleClientOptions? Target;
        public bool LateWrite, DelayStartup, FailCleanup, Cleaned;
        public Func<byte[], Task>? Received;
        public Func<CancellationToken, Task>? BeforeReply;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation)
        {
            options.Validate(); Reads++; Target = options;
            if (BeforeReply is not null) await BeforeReply(cancellation);
            return Bytes;
        }
        public async Task WriteAsync(BleClientOptions options, byte[] value, BleWriteMode mode, CancellationToken cancellation)
        {
            options.Validate();
            Writes++; Target = options; Bytes = value;
            if (BeforeReply is not null) await BeforeReply(cancellation);
            if (!LateWrite) return;
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { await Task.Delay(30); Cleaned = true; }
        }
        public async Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
        {
            Received = received;
            Entered.TrySetResult();
            if (!DelayStartup) started();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally
            {
                await Task.Delay(30);
                started(); await received([99]); Cleaned = true;
                if (FailCleanup) throw new InvalidOperationException("Unsubscribe failed");
            }
        }
        public Task ServeAsync(BleServerOptions options, Action started, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
