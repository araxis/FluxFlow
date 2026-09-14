using System.Text.Json;
using FluxFlow.Components.Mqtt.Nodes;
using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttPublishJsonTests
{
    [Fact]
    public void Typed_request_keeps_identity_and_generated_json_round_trips()
    {
        MqttPublishMessage request = new() { Topic = "test/topic", Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }) };
        MqttPublishJson.Read(request).ShouldBeSameAs(request);
        FlowValue portable = new JsonFlowValue(request.Json);
        var restored = MqttPublishJson.Read(portable);
        restored.ShouldNotBeSameAs(request);
        restored.Json.GetRawText().ShouldBe(request.Json.GetRawText());
    }

    [Theory]
    [InlineData("{\"nested\":[1,true,\"hello\"]}")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("false")]
    public void Json_content_is_encoded_as_json_bytes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var input = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { topic = "test", content = document.RootElement }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var original = input.Json.GetRawText();
        var result = MqttPublishJson.Read(input);
        result.Content.ContentType.ShouldBe("application/json");
        result.Content.Encoding.ShouldBe("utf-8");
        Encoding.UTF8.GetString(result.Content.Bytes.ToArray()).ShouldBe(json);
        input.Json.GetRawText().ShouldBe(original);
    }

    [Fact]
    public void Binary_content_and_optional_publish_fields_are_preserved()
    {
        var bytes = new byte[] { 0, 255, 128, 42 };
        var input = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new
        {
            TOPIC = "binary",
            CONTENT = FlowContent.FromBytes(bytes, "application/octet-stream"),
            QOS = 2,
            RETAIN = true,
            RESPONSETOPIC = "reply",
            CORRELATIONDATA = "request-1",
            USERPROPERTIES = new Dictionary<string, string> { ["key"] = "value" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var result = MqttPublishJson.Read(input);
        result.Content.Bytes.ToArray().ShouldBe(bytes);
        result.Content.ContentType.ShouldBe("application/octet-stream");
        result.ResponseTopic.ShouldBe("reply");
        result.CorrelationData.ShouldBe("request-1");
        result.UserProperties["key"].ShouldBe("value");
        ((int)result.Qos).ShouldBe(2);
        result.Retain.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{\"topic\":\"test\"}")]
    [InlineData("{\"content\":\"text\"}")]
    [InlineData("{\"topic\":\"test\",\"content\":{\"bytes\":\"invalid\",\"formatVersion\":1}}")]
    public void Invalid_requests_keep_the_mqtt_error_contract(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = Should.Throw<JsonException>(() => MqttPublishJson.Read((FlowValue)new JsonFlowValue(document.RootElement)));
    }

    [Fact]
    public void Read_accepts_a_structural_publish_request_and_normalizes_text_content()
    {
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new
        {
            topic = "orders/created",
            content = "payload",
            qos = 1,
            retain = true
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var result = MqttPublishJson.Read(value);

        result.Topic.ShouldBe("orders/created");
        result.Qos.ShouldBe(MqttQos.AtLeastOnce);
        result.Retain.ShouldBeTrue();
        result.Content.ContentType.ShouldBe("text/plain");
        Encoding.UTF8.GetString(result.Content.Bytes.ToArray()).ShouldBe("payload");
    }

    [Fact]
    public void Read_rejects_a_non_object_with_mqtt_owned_error()
    {
        var result = Should.Throw<JsonException>(() => MqttPublishJson.Read((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));

    }
}
