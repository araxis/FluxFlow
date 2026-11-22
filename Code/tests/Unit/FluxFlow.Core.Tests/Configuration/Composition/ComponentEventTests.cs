using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using System.Threading.Tasks.Dataflow;
using System.Text.Json;
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
    public async Task Premature_event_output_completion_does_not_strand_forwarding()
    {
        var source = new BufferBlock<FlowMessage>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new ComponentEventBridge(ComponentAddress.Create("Orders", "Validate"), source, done.Task);
        ((IDataflowBlock)bridge.Output).Complete();
        await bridge.Output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        source.Post(FlowMessage.Create(JsonFlowValue.Null));
        done.TrySetResult();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        error.Message.ShouldContain("event output completed");
        await Should.ThrowAsync<InvalidOperationException>(() => bridge.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Event_address_enrichment_preserves_identity_and_original_headers()
    {
        var source = new BufferBlock<FlowMessage>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bridge = new ComponentEventBridge(ComponentAddress.Create("Orders", "Validate"), source, done.Task);
        var received = new BufferBlock<FlowMessage>();
        using var link = bridge.Output.LinkTo(received);
        var payload = JsonFlowValue.Null;
        FlowMessage message = FlowMessage.Create(payload, correlationId: CorrelationId.New(),
            causationId: MessageId.New(), headers: new Dictionary<string, FlowValue>
            {
                ["custom"] = "keep",
                [FlowEventHeaders.Source] = "original"
            });
        try
        {
            await source.SendAsync(message);
            var result = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
            result.Value.ShouldBeSameAs(payload);
            result.MessageId.ShouldBe(message.MessageId);
            result.TraceId.ShouldBe(message.TraceId);
            result.CorrelationId.ShouldBe(message.CorrelationId);
            result.CausationId.ShouldBe(message.CausationId);
            result.Timestamp.ShouldBe(message.Timestamp);
            result.Headers["custom"].ShouldBeSameAs(message.Headers["custom"]);
            result.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("Orders.Validate");
            result.Headers[FlowEventHeaders.Workflow].Json.GetString().ShouldBe("Orders");
            result.Headers[FlowEventHeaders.Component].Json.GetString().ShouldBe("Validate");
            message.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("original");
            message.Headers.ContainsKey(FlowEventHeaders.Workflow).ShouldBeFalse();
        }
        finally { done.TrySetResult(); }
        await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Faulted_event_output_releases_pending_transform_results()
    {
        var source = new BufferBlock<FlowMessage>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new ComponentEventBridge(ComponentAddress.Create("Orders", "Validate"), source, done.Task);
        var failure = new InvalidOperationException("event predicate failed");
        using var link = bridge.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>(), new(), _ => throw failure);
        for (var i = 0; i < 512; i++) source.Post(FlowMessage.Create(JsonFlowValue.Null));
        try
        {
            var error = await Should.ThrowAsync<Exception>(() => bridge.Output.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            (error is AggregateException aggregate ? aggregate.GetBaseException() : error).ShouldBeSameAs(failure);
        }
        finally { done.TrySetResult(); }
        var completionError = await Should.ThrowAsync<Exception>(() => bridge.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        (completionError is AggregateException nested ? nested.GetBaseException() : completionError).ShouldBeSameAs(failure);
        await Should.ThrowAsync<Exception>(() => bridge.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Event_observation_keeps_the_latest_event_emitted_before_attachment()
    {
        var node = new EventNode();
        await using var provider = CreateProvider(node);
        await using var app = await provider.BuildApplicationAsync();
        var activity = provider.GetWorkflowServices("Orders").GetRequiredKeyedService<ComponentEventBridge>("Orders.Validate." + EventPortName).Output;
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
            Name = "validation.completed",
            Level = FlowEventLevel.Warning,
            Message = "Validation completed with warnings.",
            Attributes = new Dictionary<string, object?>
            {
                ["valid"] = true,
                ["count"] = 2,
                ["bytes"] = new byte[] { 1, 2, 3 }
            }
        }.ToMessage(correlationId)).ShouldBeTrue();

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
        message.ToFlowEvent().Attributes["count"].ShouldBe(2);
        message.ToFlowEvent().Attributes["bytes"].ShouldBeOfType<JsonElement>().GetString().ShouldBe("AQID");

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
        return new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("Orders.Validate"), _ => node)
            .AddEventPort<EventNode>(ComponentAddress.Parse("Orders.Validate"), EventPortName, n => n.Events).BuildServiceProvider();
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
