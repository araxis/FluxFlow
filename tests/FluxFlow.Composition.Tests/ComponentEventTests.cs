using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentEventTests
{
    private const string EventPortName = "Diagnostics";

    [Fact]
    public async Task Component_event_bridge_fans_out_every_event_in_order_to_two_subscribers()
    {
        var node = new EventNode();
        var instance = await ActivateAsync(node);
        var output = instance.Outputs[EventPortName]
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

        await instance.DisposeAsync();
    }

    [Fact]
    public async Task HasEvents_uses_custom_name_and_preserves_component_event_envelope()
    {
        var node = new EventNode();
        var instance = await ActivateAsync(node);
        var output = instance.Outputs[EventPortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        var correlationId = CorrelationId.New();
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var receive = output.Source.ReceiveAsync();

        node.Events.Post(new FlowEvent
        {
            Timestamp = occurredAt,
            CorrelationId = correlationId,
            Name = "validation.completed",
            Level = FlowEventLevel.Warning,
            Message = "Validation completed with warnings.",
            Attributes = new Dictionary<string, object?>
            {
                ["valid"] = true,
                ["count"] = 2,
                ["bytes"] = new byte[] { 1, 2, 3 }
            }
        }).ShouldBeTrue();

        var message = await receive.WaitAsync(TimeSpan.FromSeconds(5));

        message.CorrelationId.ShouldBe(correlationId);
        message.TraceId.IsEmpty.ShouldBeFalse();
        message.MessageId.IsEmpty.ShouldBeFalse();
        message.Timestamp.ShouldBeGreaterThanOrEqualTo(occurredAt);
        message.Value.ComponentAddress.ShouldBe("Orders.Validate");
        message.Value.Timestamp.ShouldBe(occurredAt);
        message.Value.Name.ShouldBe("validation.completed");
        message.Value.Level.ShouldBe(FlowEventLevel.Warning);
        message.Value.Message.ShouldBe("Validation completed with warnings.");
        message.Value.Attributes["valid"].ShouldBe("true");
        message.Value.Attributes["count"].ShouldBe("2");
        message.Value.Attributes["bytes"].ShouldBe("AQID");

        node.Complete();
        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Component_faults_remain_on_completion_and_do_not_fault_the_events_output()
    {
        var node = new EventNode();
        var instance = await ActivateAsync(node);
        var output = instance.Outputs[EventPortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();

        node.Fault(new InvalidOperationException("component failed"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await instance.Completion);
        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        output.Source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Unconsumed_component_events_do_not_hold_completion_open()
    {
        var node = new EventNode();
        var instance = await ActivateAsync(node);
        var output = instance.Outputs[EventPortName]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        node.Events.Post(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = "unconsumed"
        }).ShouldBeTrue();

        node.Complete();

        await output.Source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await instance.DisposeAsync();
    }

    private static async ValueTask<ComponentInstance> ActivateAsync(EventNode node)
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("sample.component", component =>
            component
                .UseFactory(_ => node)
                .HasEvents(EventPortName, static activated => activated.Events));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        return await descriptor.Factory(new ComponentActivationContext(
            provider,
            "Orders",
            "Validate",
            new ComponentDefinition("sample.component")));
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

}
