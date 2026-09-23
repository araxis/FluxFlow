using FluxFlow.Core.Nodes;
using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class StructuredHeaderTests
{
    [Fact]
    public void Deriving_and_branching_retain_typed_headers_without_materializing_json()
    {
        var customer = new CustomerValue();
        var headers = new Dictionary<string, FlowValue> { ["customer"] = customer, ["source"] = "orders" };
        var input = FlowMessage.Create(JsonFlowValue.Null, headers: headers);
        headers["customer"] = JsonFlowValue.Null;
        var left = input.WithValue(JsonFlowValue.Null);
        var right = input.WithValue(new FlowError("failed", "Expected", "test"));
        left.Headers["customer"].ShouldBeSameAs(customer);
        right.Headers["customer"].ShouldBeSameAs(customer);
        customer.JsonBuilds.ShouldBe(0);
        var replacement = left.Headers.ToDictionary(x => x.Key, x => x.Value);
        replacement["customer"] = JsonFlowValue.Null;
        var changed = left.WithValue(JsonFlowValue.Null, replacement);
        changed.Headers["customer"].ShouldBeSameAs(JsonFlowValue.Null);
        right.Headers["customer"].ShouldBeSameAs(customer);
        customer.JsonBuilds.ShouldBe(0);
    }

    [Fact]
    public void Json_preserves_structured_header_shapes_and_explicit_null()
    {
        var customer = new CustomerValue();
        var message = FlowMessage.Create(JsonFlowValue.Null, headers: new Dictionary<string, FlowValue>
        {
            ["customer"] = customer,
            ["text"] = "orders",
            ["number"] = new JsonFlowValue(JsonSerializer.SerializeToElement(42)),
            ["array"] = new JsonFlowValue(JsonSerializer.SerializeToElement(new[] { true, false })),
            ["nothing"] = JsonFlowValue.Null
        });
        var json = JsonSerializer.Serialize<FlowMessage>(message);
        var restored = JsonSerializer.Deserialize<FlowMessage>(json)!;
        restored.Headers["customer"].Json.GetProperty("id").GetInt32().ShouldBe(73);
        restored.Headers["text"].Json.GetString().ShouldBe("orders");
        restored.Headers["number"].Json.GetInt32().ShouldBe(42);
        restored.Headers["array"].Json[0].GetBoolean().ShouldBeTrue();
        restored.Headers["nothing"].Json.ValueKind.ShouldBe(JsonValueKind.Null);
        restored.MessageId.ShouldBe(message.MessageId);
        customer.JsonBuilds.ShouldBe(1);
        JsonSerializer.Serialize(restored).ShouldBe(json);
    }

    [Fact]
    public void Capturing_trace_context_leaves_business_headers_lazy()
    {
        var customer = new CustomerValue();
        using var activity = new Activity("header-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var message = FlowMessage.Create(JsonFlowValue.Null,
            headers: new Dictionary<string, FlowValue> { ["customer"] = customer });
        message.Headers["traceparent"].Json.GetString().ShouldBe(activity.Id);
        message.Headers["customer"].ShouldBeSameAs(customer);
        customer.JsonBuilds.ShouldBe(0);
    }

    private sealed class CustomerValue : FlowValue
    {
        public int JsonBuilds { get; private set; }
        protected override JsonElement CreateJson()
        {
            JsonBuilds++;
            return JsonSerializer.SerializeToElement(new { id = 73 });
        }
    }
}
