using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowOutputProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    [Fact]
    public async Task Unlinked_buffer_is_observed_without_consuming_or_reserving_its_message()
    {
        var output = new BufferBlock<FlowMessage>();
        using var probe = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        var message = Message(1);
        output.Post(message).ShouldBeTrue();
        (await probe.Messages.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        output.Count.ShouldBe(1);
        (await output.ReceiveAsync(Timeout)).ShouldBeSameAs(message);

        probe.Dispose();
        await probe.Messages.Completion.WaitAsync(Timeout);
        output.Post(Message(2)).ShouldBeTrue();
        (await output.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(2);
        output.Complete();
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Observation_preserves_business_backpressure_and_postponed_delivery()
    {
        var output = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var business = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        using var link = output.LinkTo(business, new DataflowLinkOptions { PropagateCompletion = true });
        using var probe = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        await output.SendAsync(Message(1)).WaitAsync(Timeout);
        (await business.OutputAvailableAsync().WaitAsync(Timeout)).ShouldBeTrue();
        (await probe.Messages.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(1);
        await output.SendAsync(Message(2)).WaitAsync(Timeout);
        (await probe.Messages.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(2);
        var pendingSend = output.SendAsync(Message(3));
        pendingSend.IsCompleted.ShouldBeFalse();
        (await business.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(1);
        (await pendingSend.WaitAsync(Timeout)).ShouldBeTrue();
        (await business.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(2);
        (await business.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(3);
        output.Complete();
        await Task.WhenAll(output.Completion, business.Completion).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Slow_probe_drops_samples_without_affecting_other_probes_or_business_delivery()
    {
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var business = new BufferBlock<FlowMessage>();
        using var businessLink = output.LinkTo(business, new DataflowLinkOptions { PropagateCompletion = true });
        using var slow = new FlowOutputProbe(output, 1, PortAddress.Parse("main.source.Output"));
        using var fast = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        for (var value = 0; value < 50; value++)
        {
            var message = Message(value);
            output.Post(message).ShouldBeTrue();
            (await business.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
            (await fast.Messages.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        }
        slow.DroppedCount.ShouldBe(49);
        fast.DroppedCount.ShouldBe(0);
        (await slow.Messages.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(0);

        slow.Dispose();
        slow.Dispose();
        output.Post(Message(50)).ShouldBeTrue();
        (await business.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(50);
        (await fast.Messages.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(50);
        slow.Messages.TryReceive(out _).ShouldBeFalse();
        slow.DroppedCount.ShouldBe(49);
        output.Complete();
        await Task.WhenAll(output.Completion, business.Completion, fast.Messages.Completion, slow.Messages.Completion).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Attaching_to_broadcast_can_replay_its_retained_message_and_dispose_discards_unread_samples()
    {
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var business = new BufferBlock<FlowMessage>();
        using var businessLink = output.LinkTo(business);
        var message = Message(1);
        output.Post(message);
        await business.ReceiveAsync(Timeout);
        using var probe = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        (await probe.Messages.OutputAvailableAsync().WaitAsync(Timeout)).ShouldBeTrue();
        probe.Dispose();
        probe.Messages.TryReceive(out _).ShouldBeFalse();
        await probe.Messages.Completion.WaitAsync(Timeout);
        using var next = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        (await next.Messages.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        output.Complete();
        await Task.WhenAll(output.Completion, next.Messages.Completion).WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_termination_reaches_a_waiting_probe_reader(bool fault)
    {
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        using var probe = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        var waiting = probe.Messages.OutputAvailableAsync();
        var error = new InvalidOperationException("Source failed.");
        if (fault)
        {
            ((IDataflowBlock)output).Fault(error);
            (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Timeout))).ShouldBeSameAs(error);
            await Should.ThrowAsync<Exception>(() => probe.Messages.Completion.WaitAsync(Timeout));
        }
        else
        {
            output.Complete();
            await Task.WhenAll(output.Completion, probe.Messages.Completion).WaitAsync(Timeout);
        }
        (await waiting.WaitAsync(Timeout)).ShouldBeFalse();
    }

    [Fact]
    public async Task Faulting_the_probe_buffer_does_not_fault_the_observed_output()
    {
        var output = new BufferBlock<FlowMessage>();
        var business = new BufferBlock<FlowMessage>();
        using var link = output.LinkTo(business, new DataflowLinkOptions { PropagateCompletion = true });
        using var probe = new FlowOutputProbe(output, 8, PortAddress.Parse("main.source.Output"));
        var error = new InvalidOperationException("Observer failed.");
        probe.Messages.Fault(error);
        (await Should.ThrowAsync<InvalidOperationException>(() => probe.Messages.Completion.WaitAsync(Timeout))).ShouldBeSameAs(error);
        var message = Message(1);
        output.Post(message).ShouldBeTrue();
        (await business.ReceiveAsync(Timeout)).ShouldBeSameAs(message);
        output.Complete();
        await Task.WhenAll(output.Completion, business.Completion).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Concurrent_attach_and_dispose_do_not_steal_messages_or_stop_the_source()
    {
        var output = new BufferBlock<FlowMessage>();
        var received = new List<int>();
        var business = new ActionBlock<FlowMessage>(message => received.Add(message.Value.Json.GetInt32()));
        using var businessLink = output.LinkTo(business, new DataflowLinkOptions { PropagateCompletion = true });
        var publish = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++) output.Post(Message(i)).ShouldBeTrue();
        });
        var observe = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                using var probe = new FlowOutputProbe(output, 1, PortAddress.Parse("main.source.Output"));
                await Task.WhenAll(Task.Run(probe.Dispose), Task.Run(probe.Dispose));
                await probe.Messages.Completion.WaitAsync(Timeout);
                probe.Messages.TryReceive(out _).ShouldBeFalse();
            }
        });
        await Task.WhenAll(publish, observe).WaitAsync(Timeout);
        output.Complete();
        await Task.WhenAll(output.Completion, business.Completion).WaitAsync(Timeout);
        received.ShouldBe(Enumerable.Range(0, 1000));
    }

    [Fact]
    public void Invalid_or_undeclared_outputs_reject_observation_before_resolving_a_component()
    {
        var address = ComponentAddress.Parse("main.node");
        var services = new ServiceCollection();
        services.AddComponentFactory(address, _ => throw new InvalidOperationException("Must not activate."));
        services.AddInputPort<IFlowNode>(address, "Input", _ => throw new InvalidOperationException("Must not resolve."));
        services.AddOutputPort<IFlowNode>(address, "Output", _ => throw new InvalidOperationException("Must not resolve."));
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>("host.node.Output", (_, _) => throw new InvalidOperationException("Unrelated service."));
        using var provider = services.BuildServiceProvider();
        Should.Throw<ArgumentOutOfRangeException>(() => provider.ObserveOutput(address.Port("Output"), 0));
        Should.Throw<ArgumentOutOfRangeException>(() => provider.ObserveOutput(address.Port("Output"), -1));
        Should.Throw<KeyNotFoundException>(() => provider.ObserveOutput(address.Port("Input")));
        Should.Throw<KeyNotFoundException>(() => provider.ObserveOutput(address.Port("Missing")));
        Should.Throw<KeyNotFoundException>(() => provider.ObserveOutput(PortAddress.Parse("host.node.Output")));
    }
}
