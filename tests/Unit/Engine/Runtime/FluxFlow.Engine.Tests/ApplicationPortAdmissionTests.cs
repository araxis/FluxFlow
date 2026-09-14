using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationPortRuntimeTests
{
    private static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Public_async_send_waits_while_try_send_remains_immediate(bool typed, int surface)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddInput(Input, capacity: 2).Build();
        var ports = new ApplicationPorts(() => runtime);
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var occupied = Message("occupied");
        target.Post(occupied).ShouldBeTrue();
        var attachment = await runtime.AttachInputAsync(Input, target);
        var typedMessages = Enumerable.Range(0, 4).Select(value => FlowMessage.Create(value)).ToArray();
        var messages = typedMessages.Select(message => message.ConvertValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        ValueTask<PortSendResult> Send(int index) => surface switch
        {
            0 => typed ? runtime.SendAsync(Input, typedMessages[index]) : runtime.SendAsync(Input, messages[index]),
            1 => typed ? ports.SendAsync(Input, typedMessages[index]) : ports.SendAsync(Input, messages[index]),
            _ => typed ? ports.SendAsync(Input.Value, typedMessages[index]) : ports.SendAsync(Input.Value, messages[index])
        };
        PortSendResult TrySend(int index) => surface switch
        {
            0 => typed ? runtime.TrySend(Input, typedMessages[index]) : runtime.TrySend(Input, messages[index]),
            1 => typed ? ports.TrySend(Input, typedMessages[index]) : ports.TrySend(Input, messages[index]),
            _ => typed ? ports.TrySend(Input.Value, typedMessages[index]) : ports.TrySend(Input.Value, messages[index])
        };
        try
        {
            (await Send(0)).Status.ShouldBe(PortSendStatus.Accepted);
            (await Send(1)).Status.ShouldBe(PortSendStatus.Accepted);
            var waiting = Send(2).AsTask();
            waiting.IsCompleted.ShouldBeFalse();
            var immediate = TrySend(3);
            immediate.Status.ShouldBe(PortSendStatus.Full);
            immediate.Port.ShouldBe(Input);
            var rejected = await runtime.Rejections.ReceiveAsync().WaitAsync(AdmissionTimeout);
            rejected.Reason.ShouldBe(ApplicationPortRejectionReason.Full);
            rejected.Port.ShouldBe(Input);
            rejected.MessageId.ShouldBe(messages[3].MessageId);
            rejected.TraceId.ShouldBe(messages[3].TraceId);

            (await target.ReceiveAsync().WaitAsync(AdmissionTimeout)).ShouldBeSameAs(occupied);
            (await waiting.WaitAsync(AdmissionTimeout)).Status.ShouldBe(PortSendStatus.Accepted);
            // Admission finishes while downstream still holds the first message.
            target.Count.ShouldBe(1);
            runtime.Complete();
            for (var index = 0; index < 3; index++)
            {
                var delivered = await target.ReceiveAsync().WaitAsync(AdmissionTimeout);
                delivered.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(index);
                delivered.MessageId.ShouldBe(messages[index].MessageId);
                delivered.TraceId.ShouldBe(messages[index].TraceId);
                delivered.Headers.ShouldBeSameAs(messages[index].Headers);
                if (!typed) delivered.ShouldBeSameAs(messages[index]);
            }
            await runtime.Completion.WaitAsync(AdmissionTimeout);
            target.TryReceive(out _).ShouldBeFalse();
            runtime.Status.Ports.Single(port => port.Address == Input).PendingMessages.ShouldBe(0);
            var late = TrySend(3);
            late.Status.ShouldBe(PortSendStatus.Completed);
        }
        finally
        {
            await runtime.DisposeAsync();
            await attachment.DisposeAsync();
            target.Complete();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_signal_send_preserves_canonical_and_typed_admission(bool typed)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddSignalInput(Signal, capacity: 2).Build();
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var occupied = Message("occupied");
        target.Post(occupied).ShouldBeTrue();
        var attachment = await runtime.AttachSignalInputAsync(Signal, target);
        var first = Message("first");
        var second = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var third = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(true, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        try
        {
            (await runtime.SendAsync(Signal, first)).IsAccepted.ShouldBeTrue();
            (await runtime.SendAsync(Signal, second)).IsAccepted.ShouldBeTrue();
            var waiting = SendThirdAsync();
            waiting.IsCompleted.ShouldBeFalse();
            runtime.TrySend(Signal, FlowMessage.Create("overflow")).Status.ShouldBe(PortSendStatus.Full);
            (await target.ReceiveAsync().WaitAsync(AdmissionTimeout)).ShouldBeSameAs(occupied);
            (await waiting.WaitAsync(AdmissionTimeout)).ShouldBeTrue();
            target.Count.ShouldBe(1);
            runtime.Complete();
            (await target.ReceiveAsync().WaitAsync(AdmissionTimeout)).ShouldBeSameAs(first);
            (await target.ReceiveAsync().WaitAsync(AdmissionTimeout)).ShouldBeSameAs(second);
            var receivedThird = await target.ReceiveAsync().WaitAsync(AdmissionTimeout);
            receivedThird.ShouldBe(third);
            receivedThird.Value.ShouldBeSameAs(third.Value);
            if (!typed) receivedThird.ShouldBeSameAs(third);
            await runtime.Completion.WaitAsync(AdmissionTimeout);
            target.TryReceive(out _).ShouldBeFalse();
            (await runtime.SendAsync(Signal, third)).IsAccepted.ShouldBeFalse();
            runtime.TrySend(Signal, third).Status.ShouldBe(PortSendStatus.Completed);
        }
        finally
        {
            await runtime.DisposeAsync();
            await attachment.DisposeAsync();
            target.Complete();
        }

        async Task<bool> SendThirdAsync() => (typed
            ? await runtime.SendAsync(Signal, third.ConvertValue<FlowValue>(third.Value!))
            : await runtime.SendAsync(Signal, third)).IsAccepted;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Public_send_preserves_error_envelopes_and_rejects_invalid_inputs(bool immediate, bool signal)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddInput(Input).AddSignalInput(Signal).Build();
        var address = signal ? Signal : Input;
        var error = new FlowError("input.invalid", "Input validation failed.", "validation");
        var canonical = FlowMessage.RestoreError(error, new MessageId("error-message"), new TraceId("error-trace"),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), new CorrelationId("error-exchange"),
            new MessageId("error-cause"), new Dictionary<string, string> { ["source"] = "admission" });
        var typed = canonical.ConvertError<string>();
        var messages = new BufferBlock<FlowMessage>();
        var signals = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var attachment = signal ? await runtime.AttachSignalInputAsync(address, signals)
            : await runtime.AttachInputAsync(address, messages);
        try
        {
            var first = immediate ? runtime.TrySend(address, canonical) : await runtime.SendAsync(address, canonical);
            first.Status.ShouldBe(PortSendStatus.Accepted);
            var second = immediate ? runtime.TrySend(address, typed) : await runtime.SendAsync(address, typed);
            second.Status.ShouldBe(PortSendStatus.Accepted);
            FlowMessage receivedCanonical;
            FlowMessage receivedTyped;
            var target = signal ? signals : messages;
            receivedCanonical = await target.ReceiveAsync().WaitAsync(AdmissionTimeout);
            receivedCanonical.ShouldBeSameAs(canonical);
            receivedTyped = await target.ReceiveAsync().WaitAsync(AdmissionTimeout);
            foreach (var received in new[] { receivedCanonical, receivedTyped })
            {
                received.ShouldBe(canonical);
                received.IsError.ShouldBeTrue();
                received.Value.ShouldBeSameAs(received.Error);
                received.Error.ShouldBeSameAs(error);
                received.Headers.ShouldBeSameAs(canonical.Headers);
            }
            if (immediate)
            {
                Should.Throw<ArgumentNullException>(() => runtime.TrySend(address, (FlowMessage)null!))
                    .ParamName.ShouldBe("message");
                Should.Throw<ArgumentNullException>(() => runtime.TrySend<string>(address, null!))
                    .ParamName.ShouldBe("message");
                Should.Throw<KeyNotFoundException>(() => runtime.TrySend(Output, canonical)).Message.ShouldContain(Output.Value);
                Should.Throw<KeyNotFoundException>(() => runtime.TrySend(Output, typed)).Message.ShouldContain(Output.Value);
            }
            else
            {
                await Should.ThrowAsync<ArgumentNullException>(async () => await runtime.SendAsync(address, (FlowMessage)null!));
                await Should.ThrowAsync<ArgumentNullException>(async () => await runtime.SendAsync<string>(address, null!));
                await Should.ThrowAsync<KeyNotFoundException>(async () => await runtime.SendAsync(Output, canonical));
                await Should.ThrowAsync<KeyNotFoundException>(async () => await runtime.SendAsync(Output, typed));
            }
        }
        finally
        {
            await runtime.DisposeAsync();
            await attachment.DisposeAsync();
            messages.Complete();
            signals.Complete();
        }
    }


}
