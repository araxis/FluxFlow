using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentEventTests
{
    [Fact]
    public async Task Component_event_bridge_fans_out_every_event_in_order_to_two_subscribers()
    {
        var node = new EventNode();
        var registration = new ComponentDescriptor(
            "sample.component",
            _ => ValueTask.FromResult(ComponentInstance.Create(node, events: node.Events)));
        var descriptor = await registration.Factory(new ComponentActivationContext(
            EmptyServiceProvider.Instance,
            "Orders",
            "Validate",
            new ComponentDefinition("sample.component")));
        var output = descriptor.Outputs[ComponentEvents.PortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        var first = new BufferBlock<FlowMessage<ComponentEvent>>();
        var second = new BufferBlock<FlowMessage<ComponentEvent>>();
        using var firstLink = output.Source.LinkTo(
            first,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var secondLink = output.Source.LinkTo(
            second,
            new DataflowLinkOptions { PropagateCompletion = true });

        foreach (var name in new[] { "validation.started", "validation.checked", "validation.completed" })
        {
            node.Events.Post(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Name = name
            }).ShouldBeTrue();
        }

        var firstMessages = new[]
        {
            await first.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await first.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await first.ReceiveAsync(TimeSpan.FromSeconds(5))
        };
        var secondMessages = new[]
        {
            await second.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await second.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await second.ReceiveAsync(TimeSpan.FromSeconds(5))
        };

        node.Complete();
        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first.Completion, second.Completion)
            .WaitAsync(TimeSpan.FromSeconds(5));

        firstMessages.Select(static message => message.Value.Name)
            .ShouldBe(["validation.started", "validation.checked", "validation.completed"]);
        secondMessages.Select(static message => message.Value.Name)
            .ShouldBe(["validation.started", "validation.checked", "validation.completed"]);
        secondMessages.Select(static message => message.MessageId)
            .ShouldBe(firstMessages.Select(static message => message.MessageId));

        await descriptor.DisposeAsync();
    }

    [Fact]
    public async Task Registered_factories_expose_traced_addressable_component_events()
    {
        var node = new EventNode();
        var registration = new ComponentDescriptor(
            "sample.component",
            _ => ValueTask.FromResult(ComponentInstance.Create(node, events: node.Events)));
        var context = new ComponentActivationContext(
            EmptyServiceProvider.Instance,
            "Orders",
            "Validate",
            new ComponentDefinition("sample.component"));
        var descriptor = await registration.Factory(context);
        var output = descriptor.Outputs[ComponentEvents.PortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        var correlationId = CorrelationId.New();
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        node.Events.Post(new FlowEvent
        {
            Timestamp = occurredAt,
            CorrelationId = correlationId,
            Name = "validation.completed",
            Attributes = new Dictionary<string, object?>
            {
                ["valid"] = true,
                ["count"] = 2
            }
        }).ShouldBeTrue();

        var message = await output.Source.ReceiveAsync(TimeSpan.FromSeconds(5));

        message.CorrelationId.ShouldBe(correlationId);
        message.TraceId.IsEmpty.ShouldBeFalse();
        message.MessageId.IsEmpty.ShouldBeFalse();
        message.Timestamp.ShouldBeGreaterThanOrEqualTo(occurredAt);
        message.Value.ComponentAddress.ShouldBe("Orders.Validate");
        message.Value.Timestamp.ShouldBe(occurredAt);
        message.Value.Name.ShouldBe("validation.completed");
        message.Value.Attributes["valid"].ShouldBe("true");
        message.Value.Attributes["count"].ShouldBe("2");

        node.Complete();
        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await descriptor.DisposeAsync();
    }

    [Fact]
    public async Task Component_faults_remain_on_completion_and_do_not_fault_the_events_output()
    {
        var node = new EventNode();
        var registration = new ComponentDescriptor(
            "sample.component",
            _ => ValueTask.FromResult(ComponentInstance.Create(node, events: node.Events)));
        var descriptor = await registration.Factory(new ComponentActivationContext(
            EmptyServiceProvider.Instance,
            "Orders",
            "Validate",
            new ComponentDefinition("sample.component")));
        var output = descriptor.Outputs[ComponentEvents.PortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();

        node.Fault(new InvalidOperationException("component failed"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await descriptor.Completion);
        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        output.Source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await descriptor.DisposeAsync();
    }

    [Fact]
    public async Task Unconsumed_component_events_do_not_hold_completion_open()
    {
        var node = new EventNode();
        var registration = new ComponentDescriptor(
            "sample.component",
            _ => ValueTask.FromResult(ComponentInstance.Create(node, events: node.Events)));
        var descriptor = await registration.Factory(new ComponentActivationContext(
            EmptyServiceProvider.Instance,
            "Orders",
            "Validate",
            new ComponentDefinition("sample.component")));
        var output = descriptor.Outputs[ComponentEvents.PortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        node.Events.Post(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = "unconsumed"
        }).ShouldBeTrue();

        node.Complete();

        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await descriptor.DisposeAsync();
    }

    private sealed class EventNode : IFlowNode
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BufferBlock<FlowEvent> Events { get; } = new();

        public Task Completion => _completion.Task;

        public void Complete()
        {
            Events.Complete();
            _completion.TrySetResult();
        }

        public void Fault(Exception exception)
        {
            ((IDataflowBlock)Events).Fault(exception);
            _completion.TrySetException(exception);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
