using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class NativeControlInputTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Address = ApplicationAddress.WorkflowPort("Main", "Source", "Ack");

    [Fact]
    public async Task Control_target_views_preserve_native_postponement_and_external_ownership()
    {
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var occupied = FlowMessage.Create(FlowValue.From("occupied"));
        var routed = FlowMessage.Create(FlowValue.From("routed"));
        target.Post(occupied).ShouldBeTrue();
        var services = new ServiceCollection().AddExternalFluxFlowSignalTarget(Address, target);
        await using var provider = services.BuildServiceProvider();
        var view = provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(Address.Value);
        view.ShouldNotBeSameAs(target);
        view.Completion.ShouldBeSameAs(target.Completion);
        var source = new BufferBlock<FlowMessage>();
        using var link = source.LinkTo(view, new DataflowLinkOptions { PropagateCompletion = false });
        source.Post(routed).ShouldBeTrue();
        source.Complete();
        source.Completion.IsCompleted.ShouldBeFalse();
        (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(occupied);
        (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(routed);
        await source.Completion.WaitAsync(Deadline);
        await provider.DisposeAsync();
        target.Completion.IsCompleted.ShouldBeFalse();
        (await target.SendAsync(routed)).ShouldBeTrue();
        (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(routed);
        target.Complete();
        await target.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_control_inputs_preserve_kind_and_canonical_message(bool error)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddSignalInput(Address, 1).Build();
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        await using var attachment = await runtime.AttachSignalInputAsync(Address, target);
        var value = FlowValue.From(new { Text = "control-\u03bb", Bytes = new byte[] { 0, 128, 255 } });
        var failure = new FlowError("control.failure", "Exact failure.", "test");
        var original = FlowMessage.Restore(value, new MessageId("native-message"), new TraceId("native-trace"),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), new CorrelationId("native-exchange"),
            new MessageId("native-cause"), new Dictionary<string, string> { ["source"] = "native-test" });
        var message = error ? original.WithError(failure) : original;

        (await runtime.SendAsync(Address, message)).Status.ShouldBe(PortSendStatus.Accepted);
        var received = await target.ReceiveAsync(Deadline);
        received.ShouldBeSameAs(message);
        received.Headers.ShouldBeSameAs(message.Headers);
        received.MessageId.ShouldBe(message.MessageId);
        received.TraceId.ShouldBe(message.TraceId);
        received.CorrelationId.ShouldBe(message.CorrelationId);
        received.CausationId.ShouldBe(message.CausationId);
        received.Timestamp.ShouldBe(message.Timestamp);
        received.IsError.ShouldBe(error);
        if (error) received.Error.ShouldBeSameAs(failure);
        else received.Value.ShouldBeSameAs(value);
        runtime.Ports.Single(port => port.Address == Address).Kind.ShouldBe(ApplicationPortKind.Signal);
        runtime.Status.Ports.Single(port => port.Address == Address).Kind.ShouldBe(ApplicationPortKind.Signal);
        await Should.ThrowAsync<InvalidOperationException>(async () => await runtime.AttachInputAsync(Address, target));

        runtime.Complete();
        await runtime.Completion.WaitAsync(Deadline);
        target.Completion.IsCompleted.ShouldBeFalse();
        (await target.SendAsync(message)).ShouldBeTrue();
        (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        target.Complete();
        await target.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_input_admission_cancellation_respects_target_ownership(bool signal)
    {
        var builder = new ApplicationPortRuntimeBuilder();
        if (signal) builder.AddSignalInput(Address, 1);
        else builder.AddInput(Address, 1);
        await using var runtime = builder.Build();
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var occupied = FlowMessage.Create(FlowValue.From("occupied"));
        var admitted = FlowMessage.Create(FlowValue.From("admitted"));
        var canceled = FlowMessage.Create(FlowValue.From("canceled"));
        target.Post(occupied).ShouldBeTrue();
        await using var attachment = signal ? await runtime.AttachSignalInputAsync(Address, target)
            : await runtime.AttachInputAsync(Address, target);
        using var cancellation = new CancellationTokenSource();
        try
        {
            (await runtime.SendAsync(Address, admitted)).Status.ShouldBe(PortSendStatus.Accepted);
            var waiting = runtime.SendAsync(Address, canceled, cancellation.Token).AsTask();
            waiting.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            (await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
            runtime.TrySend(Address, canceled).Status.ShouldBe(PortSendStatus.Full);
            var rejection = await runtime.Rejections.ReceiveAsync(Deadline);
            rejection.MessageId.ShouldBe(canceled.MessageId);
            rejection.Port.ShouldBe(Address);
            rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Full);

            runtime.Complete();
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(occupied);
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(admitted);
            await runtime.Completion.WaitAsync(Deadline);
            target.TryReceive(out _).ShouldBeFalse();
            target.Completion.IsCompleted.ShouldBe(!signal);
            runtime.Status.Ports.Single(port => port.Address == Address).PendingMessages.ShouldBe(0);
            (await runtime.SendAsync(Address, canceled)).Status.ShouldBe(PortSendStatus.Completed);
        }
        finally { await runtime.DisposeAsync(); target.Complete(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_revision_replacement_preserves_native_ownership(bool abort)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddSignalInput(Address, 1).Build();
        var previous = new BufferBlock<FlowMessage>();
        var replacement = new BufferBlock<FlowMessage>();
        await using var attachment = await runtime.AttachSignalInputAsync(Address, previous);
        await using var builder = runtime.CreateRevision("replacement").ReplaceSignalInput(Address, replacement);
        await using var revision = builder.Build();
        await using var lease = await revision.ActivateAsync();
        await attachment.DisposeAsync();
        var message = FlowMessage.Create(FlowValue.From("new-revision"));
        (await runtime.SendAsync(Address, message)).IsAccepted.ShouldBeTrue();
        (await replacement.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        previous.TryReceive(out _).ShouldBeFalse();
        runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("replacement");
        if (abort) await runtime.DisposeAsync();
        else { runtime.Complete(); await runtime.Completion.WaitAsync(Deadline); }

        foreach (var target in new[] { previous, replacement })
        {
            target.Completion.IsCompleted.ShouldBeFalse();
            (await target.SendAsync(message)).ShouldBeTrue();
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            target.Complete();
            await target.Completion.WaitAsync(Deadline);
        }
    }
}
