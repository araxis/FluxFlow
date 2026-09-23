using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Application;
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
        var services = new ServiceCollection()
            .AddComponentFactory("main.first", _ => first)
            .AddInputPort<Forward>("main.first", "Input", node => node.Input)
            .AddOutputPort<Forward>("main.first", "Output", node => node.Output)
            .AddComponentFactory("main.left", _ => left)
            .AddInputPort<Capture>("main.left", "Input", node => node.Input)
            .AddComponentFactory("main.right", _ => right)
            .AddInputPort<Capture>("main.right", "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.first.Output", "main.left.Input");
            ports.Link("main.first.Output", "main.right.Input");
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
        received[0].Value.Json.GetProperty("orderId").GetInt32().ShouldBe(42);
        received[1].Value.Json.GetProperty("orderId").GetInt32().ShouldBe(42);
        payload.JsonBuilds.ShouldBe(1);
        await appExecution.StopAsync(timeout.Token);
    }

    [Fact]
    public void Generic_and_common_serialization_share_the_wire_shape_and_generated_metadata()
    {
        var message = FlowMessage.Create(new OrderValue(73),
            headers: new Dictionary<string, FlowValue> { ["original"] = new OrderValue(15) });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = TypedMessageJsonContext.Default
        };
        var json = JsonSerializer.Serialize(message, options);
        JsonSerializer.Serialize<FlowMessage>(message, options).ShouldBe(json);
        var restored = JsonSerializer.Deserialize<FlowMessage<OrderValue>>(json, options).ShouldNotBeNull();
        restored.Value.OrderId.ShouldBe(73);
        restored.MessageId.ShouldBe(message.MessageId);
        restored.TraceId.ShouldBe(message.TraceId);
        restored.Headers["original"].Json.GetProperty("orderId").GetInt32().ShouldBe(15);
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
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(message, Stopping);
    }

    private sealed class Capture : FlowNode
    {
        public TaskCompletionSource<FlowMessage> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task ProcessAsync(FlowMessage message)
        {
            Received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }
}

public sealed class OrderValue(int orderId) : FlowValue
{
    public int OrderId { get; } = orderId;
    [JsonIgnore] public int JsonBuilds { get; private set; }
    protected override JsonElement CreateJson()
    {
        JsonBuilds++;
        using var document = JsonDocument.Parse($"{{\"orderId\":{OrderId}}}");
        return document.RootElement.Clone();
    }
}

[JsonSerializable(typeof(FlowMessage<OrderValue>))]
[JsonSerializable(typeof(FlowMessage))]
[JsonSerializable(typeof(OrderValue))]
[JsonSerializable(typeof(Dictionary<string, FlowValue>))]
[JsonSerializable(typeof(FlowValue))]
[JsonSerializable(typeof(FlowError))]
[JsonSerializable(typeof(TraceId))]
[JsonSerializable(typeof(MessageId))]
[JsonSerializable(typeof(MessageId?))]
[JsonSerializable(typeof(CorrelationId?))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, FlowValue>))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTimeOffset))]
internal partial class TypedMessageJsonContext : JsonSerializerContext;
