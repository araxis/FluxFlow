using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MqttTraceCollection
{
    public const string Name = "MQTT trace context";
}

[Collection(MqttTraceCollection.Name)]
public sealed class MqttTraceContextTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_injects_current_context_without_mutating_input(bool recorded)
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = Controller(session);
        await controller.StartAsync();
        using var current = new Activity("publisher").SetIdFormat(ActivityIdFormat.W3C).Start();
        current.ActivityTraceFlags = recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        current.TraceStateString = "tenant=one";
        var original = new MqttPublishMessage
        {
            Topic = "events/one",
            Content = FlowContent.FromBytes(new byte[] { 0, 255, 17 }),
            UserProperties = new Dictionary<string, string>
            {
                ["traceparent"] = "stale",
                ["tracestate"] = "stale=one",
                ["business"] = "keep"
            }
        };
        await controller.ExecuteAsync(new MqttPublishClientRequest { Message = original });
        var sent = session.Published.ShouldHaveSingleItem();
        sent.UserProperties["traceparent"].ShouldBe(current.Id);
        sent.UserProperties["tracestate"].ShouldBe("tenant=one");
        sent.UserProperties["business"].ShouldBe("keep");
        sent.UserProperties.Count.ShouldBe(3);
        sent.Content.ShouldBeSameAs(original.Content);
        original.UserProperties["traceparent"].ShouldBe("stale");
        original.UserProperties["tracestate"].ShouldBe("stale=one");
        Activity.Current.ShouldBeSameAs(current);

        current.TraceStateString = null;
        await controller.ExecuteAsync(new MqttPublishClientRequest { Message = original });
        session.Published.Last().UserProperties.ContainsKey("tracestate").ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_without_w3c_context_keeps_original_message(bool hierarchical)
    {
        using var suppressed = FlowTracing.SuppressContext();
        using var current = hierarchical ? new Activity("legacy").SetIdFormat(ActivityIdFormat.Hierarchical).Start() : null;
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = Controller(session);
        await controller.StartAsync();
        var original = new MqttPublishMessage { Topic = "events/one", Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }) };
        await controller.ExecuteAsync(new MqttPublishClientRequest { Message = original });
        session.Published.ShouldHaveSingleItem().ShouldBeSameAs(original);
        original.UserProperties.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("invalid")]
    [InlineData(null)]
    public async Task Trigger_preserves_remote_context_across_queue(string? flags)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                    ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var startup = new Activity("unrelated-startup").SetIdFormat(ActivityIdFormat.W3C).Start();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new VNextRecordingMqttTransportSession
        {
            SubscribeHandler = (_, _, _) => { subscribed.TrySetResult(); return ValueTask.CompletedTask; }
        };
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(controller, new MqttSubscriptionTriggerOptions
        {
            TriggerId = "receiver",
            Subscriptions = [MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition { TopicFilter = "events/#" })]
        });
        await using var sink = new ContextSink();
        using var link = trigger.Output.LinkTo(sink.Input);
        await trigger.StartAsync();
        await subscribed.Task.WaitAsync(Deadline);
        var valid = flags is "00" or "01";
        var parent = valid ? $"00-0123456789abcdef0123456789abcdef-0123456789abcdef-{flags}" : flags;
        var properties = new Dictionary<string, string> { ["business"] = "keep", ["tracestate"] = "tenant=remote" };
        if (parent is not null) properties["traceparent"] = parent;
        await session.EmitAsync(new MqttReceivedApplicationMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "events/one",
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }),
            UserProperties = properties
        });
        var observed = await sink.Observed.Task.WaitAsync(Deadline);
        observed.Message.Value!.Json.Deserialize<MqttReceivedApplicationMessage>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.UserProperties["business"].ShouldBe("keep");
        observed.Message.Headers.ContainsKey("business").ShouldBeFalse();
        if (valid)
        {
            observed.Message.Headers["traceparent"].ShouldBe(parent);
            observed.Message.Headers["tracestate"].ShouldBe("tenant=remote");
            observed.Context.TraceId.ToHexString().ShouldBe("0123456789abcdef0123456789abcdef");
            observed.ParentSpan.ToHexString().ShouldBe("0123456789abcdef");
            observed.Context.TraceState.ShouldBe("tenant=remote");
            observed.Context.TraceFlags.ShouldBe(flags == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None);
        }
        else
        {
            observed.Message.Headers.ContainsKey("traceparent").ShouldBeFalse();
            observed.Message.Headers.ContainsKey("tracestate").ShouldBeFalse();
            observed.ParentSpan.ShouldBe(default);
            observed.Context.TraceId.ShouldNotBe(startup.TraceId);
        }
        Activity.Current.ShouldBeSameAs(startup);
        trigger.Complete();
        await trigger.Completion.WaitAsync(Deadline);
        sink.Complete();
        await sink.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Publish_and_trigger_link_real_processing_spans_through_transport_properties()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new VNextRecordingMqttTransportSession
        {
            SubscribeHandler = (_, _, _) => { subscribed.TrySetResult(); return ValueTask.CompletedTask; }
        };
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(controller, new MqttSubscriptionTriggerOptions
        {
            TriggerId = "receiver",
            Subscriptions = [MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition { TopicFilter = "events/#" })]
        });
        await using var sink = new ContextSink();
        await using var publisher = new MqttPublishOperationNode(controller);
        var replies = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        using var replyLink = publisher.Output.LinkTo(replies);
        using var receiveLink = trigger.Output.LinkTo(sink.Input);
        await trigger.StartAsync();
        await subscribed.Task.WaitAsync(Deadline);
        var content = FlowContent.FromBytes(new byte[] { 0, 255, 17 });
        var request = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new MqttPublishMessage { Topic = "events/one", Content = content }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        (await publisher.Input.SendAsync(request)).ShouldBeTrue();
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        var sent = session.Published.ShouldHaveSingleItem();
        ActivityContext.TryParse(sent.UserProperties["traceparent"], null, out var publishedContext).ShouldBeTrue();
        publishedContext.TraceId.ShouldBe(caller.TraceId);
        publishedContext.SpanId.ShouldNotBe(caller.SpanId);
        // Simulate only broker forwarding; actual controller, source and nodes execute.
        await session.EmitAsync(new MqttReceivedApplicationMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = sent.Topic,
            Content = sent.Content,
            UserProperties = sent.UserProperties
        });
        var observed = await sink.Observed.Task.WaitAsync(Deadline);
        observed.Context.TraceId.ShouldBe(caller.TraceId);
        observed.ParentSpan.ShouldBe(publishedContext.SpanId);
        observed.Context.SpanId.ShouldNotBe(publishedContext.SpanId);
        observed.Message.Headers["traceparent"].ShouldBe(sent.UserProperties["traceparent"]);
        observed.Message.Value!.Json.Deserialize<MqttReceivedApplicationMessage>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Content.Bytes.ToArray().ShouldBe(new byte[] { 0, 255, 17 });
        Activity.Current.ShouldBeSameAs(caller);
        trigger.Complete();
        await trigger.Completion.WaitAsync(Deadline);
        publisher.Complete();
        sink.Complete();
        await Task.WhenAll(publisher.Completion, sink.Completion).WaitAsync(Deadline);
    }

    private static MqttClientController Controller(VNextRecordingMqttTransportSession session) => new(new()
    {
        Name = "trace-test",
        ClientId = "trace-test",
        Broker = new MqttBrokerConfiguration { Host = "unused.test" },
        Reconnect = new MqttReconnectConfiguration { Enabled = false }
    }, new VNextRecordingMqttTransportFactory(() => session));

    private sealed class ContextSink : FlowNode
    {
        public TaskCompletionSource<(FlowMessage Message, ActivityContext Context, ActivitySpanId ParentSpan)> Observed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task ProcessAsync(FlowMessage message)
        {
            var activity = Activity.Current!;
            Observed.TrySetResult((message, activity.Context, activity.ParentSpanId));
            return Task.CompletedTask;
        }
    }
}
