using FluxFlow.Application.Addressing;
using FluxFlow.Application.Execution;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics;
using FluxFlow.Application;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class TypedFlowMessageTests
{
    [Fact]
    public void Header_replacement_preserves_identity_and_snapshots_without_capturing_context()
    {
        var payload = new OrderValue(12);
        var original = FlowMessage.Create(payload, correlationId: new("correlation"), causationId: new("cause"));
        var headers = new Dictionary<string, FlowValue>
        {
            ["saved"] = payload
        };
        using var unrelated = new Activity("unrelated").SetIdFormat(ActivityIdFormat.W3C).Start();
        var enriched = original.WithHeaders(headers);
        headers["saved"] = JsonFlowValue.Null;
        enriched.MessageId.ShouldBe(original.MessageId);
        enriched.TraceId.ShouldBe(original.TraceId);
        enriched.CorrelationId.ShouldBe(original.CorrelationId);
        enriched.CausationId.ShouldBe(original.CausationId);
        enriched.Timestamp.ShouldBe(original.Timestamp);
        enriched.Value.ShouldBeSameAs(payload);
        enriched.Headers["saved"].ShouldBeSameAs(payload);
        enriched.Headers.ContainsKey("traceparent").ShouldBeFalse();
        original.Headers.ContainsKey("saved").ShouldBeFalse();
        payload.JsonBuilds.ShouldBe(0);
        Should.Throw<ArgumentNullException>(() => original.WithHeaders(null!));
        original.WithValue(new FlowError("error", "failure", "test")).WithHeaders(headers).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Creating_and_deriving_capture_context_once_and_keep_payloads_lazy(bool typed)
    {
        var payload = new OrderValue(12);
        var result = new OrderValue(13);
        var trace = new TraceId("business-trace");
        var correlation = new CorrelationId("business-correlation");
        var cause = new MessageId("original-cause");
        var headers = new Dictionary<string, FlowValue>
        {
            ["business"] = payload
        };
        using var initialActivity = new Activity("initial").SetIdFormat(ActivityIdFormat.W3C).Start();
        FlowMessage initial = typed ? FlowMessage.Create(payload, correlation, trace, headers, cause) : FlowMessage.Create((FlowValue)payload, correlation, trace, headers, cause);
        headers["business"] = JsonFlowValue.Null;
        using var nextActivity = new Activity("derived").SetIdFormat(ActivityIdFormat.W3C).Start();
        FlowMessage derived = typed ? initial.WithValue(result) : initial.WithValue((FlowValue)result);
        initial.Value.ShouldBeSameAs(payload);
        initial.CausationId.ShouldBe(cause);
        initial.Headers["traceparent"].Json.GetString().ShouldBe(initialActivity.Id);
        derived.Value.ShouldBeSameAs(result);
        derived.MessageId.ShouldNotBe(initial.MessageId);
        derived.CausationId.ShouldBe(initial.MessageId);
        derived.CorrelationId.ShouldBe(correlation);
        derived.TraceId.ShouldBe(trace);
        derived.Timestamp.ShouldBeGreaterThanOrEqualTo(initial.Timestamp);
        derived.Headers["traceparent"].Json.GetString().ShouldBe(nextActivity.Id);
        derived.Headers["business"].ShouldBeSameAs(payload);
        initial.Headers["business"].ShouldBeSameAs(payload);
        payload.JsonBuilds.ShouldBe(0);
        result.JsonBuilds.ShouldBe(0);
    }

    [Fact]
    public void Typed_projection_preserves_identity_and_does_not_capture_ambient_context()
    {
        var payload = new OrderValue(12);
        var replacement = new OrderValue(13);
        var original = FlowMessage.Create(payload, correlationId: new("correlation"), causationId: new("cause"));
        using var unrelated = new Activity("unrelated").SetIdFormat(ActivityIdFormat.W3C).Start();
        var projected = new FlowMessage<OrderValue>(original, replacement);
        projected.Value.ShouldBeSameAs(replacement);
        projected.MessageId.ShouldBe(original.MessageId);
        projected.TraceId.ShouldBe(original.TraceId);
        projected.CausationId.ShouldBe(original.CausationId);
        projected.CorrelationId.ShouldBe(original.CorrelationId);
        projected.Timestamp.ShouldBe(original.Timestamp);
        projected.Headers.ShouldBeSameAs(original.Headers);
        original.Value.ShouldBeSameAs(payload);
        payload.JsonBuilds.ShouldBe(0);
        replacement.JsonBuilds.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deriving_keeps_explicit_headers_and_causation_and_rejects_null_payload(bool typed)
    {
        var input = FlowMessage.Create(JsonFlowValue.Null);
        var headers = new Dictionary<string, FlowValue>
        {
            ["custom"] = "original"
        };
        var cause = new MessageId("override");
        FlowMessage next = typed ? input.WithValue(JsonFlowValue.Null, headers, cause) : input.WithValue((FlowValue)JsonFlowValue.Null, headers, cause);
        headers["custom"] = "modified";
        next.CausationId.ShouldBe(cause);
        next.Headers["custom"].Json.GetString().ShouldBe("original");
        input.Headers.ContainsKey("custom").ShouldBeFalse();
        if (typed)
            Should.Throw<ArgumentNullException>(() => input.WithValue<OrderValue>(null!));
        else
            Should.Throw<ArgumentNullException>(() => input.WithValue((FlowValue)null!));
    }

    [Fact]
    public async Task Typed_message_crosses_common_ports_and_fanout_without_materializing_json()
    {
        var payload = new OrderValue(42);
        var message = FlowMessage.Create(payload);
        FlowMessage envelope = message;
        envelope.ShouldBeSameAs(message);
        envelope.Value.ShouldBeSameAs(payload);
        var first = new Forward();
        var left = new Capture();
        var right = new Capture();
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.first"), _ => first).AddInputPort<Forward>(ComponentAddress.Parse("main.first"), "Input", node => node.Input).AddOutputPort<Forward>(ComponentAddress.Parse("main.first"), "Output", node => node.Output).AddComponentFactory(ComponentAddress.Parse("main.left"), _ => left).AddInputPort<Capture>(ComponentAddress.Parse("main.left"), "Input", node => node.Input).AddComponentFactory(ComponentAddress.Parse("main.right"), _ => right).AddInputPort<Capture>(ComponentAddress.Parse("main.right"), "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.left.Input"));
            ports.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.right.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await appExecution.StartAsync(timeout.Token);
        (await first.Input.SendAsync(message, timeout.Token)).ShouldBeTrue();
        var received = await Task.WhenAll(left.Received.Task, right.Received.Task).WaitAsync(timeout.Token);
        foreach (var item in received)
        {
            item.ShouldBeSameAs(message);
            item.Value.ShouldBeSameAs(payload);
        }

        payload.JsonBuilds.ShouldBe(0);
        received[0].Value.Json.GetProperty("OrderId").GetInt32().ShouldBe(42);
        received[1].Value.Json.GetProperty("OrderId").GetInt32().ShouldBe(42);
        payload.JsonBuilds.ShouldBe(1);
        await appExecution.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_and_common_serialization_share_the_wire_shape_and_generated_metadata(bool explicitConverter)
    {
        var message = FlowMessage.Create(new OrderValue(73), headers: new Dictionary<string, FlowValue> { ["original"] = new OrderValue(15) });
        var options = new JsonSerializerOptions()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(TypedMessageJsonContext.Default, FlowJsonContext.Default)
        };
        if (explicitConverter)
        {
            var converter = new FlowMessageJsonConverter<OrderValue>();
            options.Converters.Add(converter);
            options.GetConverter(typeof(FlowMessage<OrderValue>)).ShouldBeSameAs(converter);
        }
        var json = JsonSerializer.Serialize(message, options);
        JsonSerializer.Serialize<FlowMessage>(message, options).ShouldBe(json);
        var restored = JsonSerializer.Deserialize<FlowMessage<OrderValue>>(json, options).ShouldNotBeNull();
        restored.Value.OrderId.ShouldBe(73);
        restored.MessageId.ShouldBe(message.MessageId);
        restored.TraceId.ShouldBe(message.TraceId);
        restored.Headers["original"].Json.GetProperty("OrderId").GetInt32().ShouldBe(15);
    }

    [Fact]
    public void Explicit_typed_converter_preserves_errors_and_envelope_validation()
    {
        var options = new JsonSerializerOptions()
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(TypedMessageJsonContext.Default, FlowJsonContext.Default),
            Converters = { new FlowMessageJsonConverter<OrderValue>() }
        };
        var typed = TestMessages.Error<OrderValue>(new FlowError("invalid", "Invalid order", "validation", details: new OrderValue(73)));
        var json = JsonSerializer.Serialize(typed, options);
        json.ShouldBe(JsonSerializer.Serialize<FlowMessage>(typed, FlowJsonContext.Default.FlowMessage));
        var restored = JsonSerializer.Deserialize<FlowMessage<OrderValue>>(json, options).ShouldNotBeNull();
        restored.IsError.ShouldBeTrue();
        restored.Error!.Details!.Json.GetProperty("OrderId").GetInt32().ShouldBe(73);
        restored.MessageId.ShouldBe(typed.MessageId);
        Should.Throw<InvalidOperationException>(() => _ = restored.Value);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlowMessage<OrderValue>>(
            json.Replace("\"IsError\":true", "\"IsError\":false"), options));
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlowMessage<OrderValue>>(
            json.Insert(1, "\"unknown\":true,"), options));
    }

    [Fact]
    public void Typed_error_keeps_the_common_error_payload_and_can_be_formatted()
    {
        var error = new FlowError("invalid", "Invalid order", "validation");
        var typed = TestMessages.Error<OrderValue>(error);
        FlowMessage message = typed;
        message.Value.ShouldBeSameAs(error);
        typed.Error.ShouldBeSameAs(error);
        Should.Throw<InvalidOperationException>(() => _ = typed.Value);
        typed.ToString().ShouldContain("IsError = True");
        JsonSerializer.Serialize(typed).ShouldBe(JsonSerializer.Serialize(message));
    }

    [Fact]
    public void Generic_contract_rejects_non_FlowValue_types()
    {
        Should.Throw<ArgumentException>(() => typeof(FlowMessage<>).MakeGenericType(typeof(string)));
        typeof(FlowMessage<OrderValue>).IsAssignableTo(typeof(FlowMessage)).ShouldBeTrue();
    }

    private sealed class Forward : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Forward()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private async Task ProcessAsync(FlowMessage message) => await EmitAsync(message, Stopping);
    }

    private sealed class Capture : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Capture()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public TaskCompletionSource<FlowMessage> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task ProcessAsync(FlowMessage message)
        {
            Received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }
}

public sealed class OrderValue(int orderId) : FlowValue
{
    public int OrderId { get; } = orderId;

    [JsonIgnore]
    public int JsonBuilds { get; private set; }

    protected override JsonElement CreateJson()
    {
        JsonBuilds++;
        return JsonSerializer.SerializeToElement(this, TypedMessageJsonContext.Default.OrderValue);
    }
}

[JsonSerializable(typeof(FlowMessage<OrderValue>))]
[JsonSerializable(typeof(OrderValue))]
internal partial class TypedMessageJsonContext : JsonSerializerContext;
