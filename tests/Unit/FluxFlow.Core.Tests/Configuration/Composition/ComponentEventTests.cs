using FluxFlow.Core.Application;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentEventTests
{
    private const string EventPortName = "Diagnostics";

    [Fact]
    public async Task Event_observation_keeps_the_latest_event_emitted_before_attachment()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        await using var app = await provider.BuildApplicationAsync();
        var activity = app.Workflows["Orders"].EventBridges[0].Activity;
        node.Events.Post(new FlowEvent { Timestamp = DateTimeOffset.UtcNow, Name = "constructed" }).ShouldBeTrue();
        (await activity.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        var received = await activity.ReceiveAsync(TimeSpan.FromSeconds(5));
        received.ToFlowEvent().Name.ShouldBe("constructed");
        received.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("Orders.Validate");
    }

    [Fact]
    public async Task Component_event_bridge_fans_out_every_event_in_order_to_two_subscribers()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        var instance = await provider.BuildApplicationAsync();
        var output = instance.GetOutput("Orders.Validate." + EventPortName);
        var first = new BufferBlock<FlowMessage>();
        var second = new BufferBlock<FlowMessage>();
        using var firstLink = output.Block.LinkTo(
            first,
            new DataflowLinkOptions { PropagateCompletion = true });
        using var secondLink = output.Block.LinkTo(
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
        await output.Block.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first.Completion, second.Completion)
            .WaitAsync(TimeSpan.FromSeconds(5));

        firstMessages.Select(static message => message.ToFlowEvent().Name)
            .ShouldBe(["validation.started", "validation.checked", "validation.completed"]);
        secondMessages.Select(static message => message.ToFlowEvent().Name)
            .ShouldBe(["validation.started", "validation.checked", "validation.completed"]);
        secondMessages.Select(static message => message.MessageId)
            .ShouldBe(firstMessages.Select(static message => message.MessageId));

        await instance.DisposeAsync();
    }

    [Fact]
    public async Task HasEvents_uses_custom_name_and_preserves_component_event_envelope()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        var instance = await provider.BuildApplicationAsync();
        var output = instance.GetOutput("Orders.Validate." + EventPortName);
        var correlationId = CorrelationId.New();
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var receive = output.Block.ReceiveAsync();

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
        message.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("Orders.Validate");
        message.ToFlowEvent().Timestamp.ShouldBe(occurredAt);
        message.ToFlowEvent().Name.ShouldBe("validation.completed");
        message.ToFlowEvent().Level.ShouldBe(FlowEventLevel.Warning);
        message.ToFlowEvent().Message.ShouldBe("Validation completed with warnings.");
        message.ToFlowEvent().Attributes["valid"].ShouldBe(true);
        message.ToFlowEvent().Attributes["count"].ShouldBe(2L);
        message.ToFlowEvent().Attributes["bytes"].ShouldBe("AQID");

        node.Complete();
        await output.Block.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Component_faults_remain_on_completion_and_do_not_fault_the_events_output()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        var instance = await provider.BuildApplicationAsync();
        var output = instance.GetOutput("Orders.Validate." + EventPortName);

        node.Fault(new InvalidOperationException("component failed"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await node.Completion);
        await output.Block.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        output.Block.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask());
        await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Unconsumed_component_events_do_not_hold_completion_open()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        var instance = await provider.BuildApplicationAsync();
        var output = instance.GetOutput("Orders.Validate." + EventPortName);
        node.Events.Post(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = "unconsumed"
        }).ShouldBeTrue();

        node.Complete();

        await output.Block.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await instance.DisposeAsync();
    }

    private static ServiceProvider CreateProvider(EventNode node)
    {
        return new ServiceCollection().AddComponentFactory("Orders.Validate", _ => node)
            .AddEventPort<EventNode>("Orders.Validate", EventPortName, n => n.Events).BuildServiceProvider();
    }

    private sealed class EventNode : IFlowNode
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BufferBlock<FlowMessage> Events { get; } = new();

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
