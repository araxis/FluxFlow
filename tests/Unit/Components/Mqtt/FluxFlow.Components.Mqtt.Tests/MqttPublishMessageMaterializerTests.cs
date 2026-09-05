using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Materialization;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttPublishMessageMaterializerTests
{
    [Fact]
    public void Materialize_accepts_a_structural_publish_request_and_normalizes_text_content()
    {
        var materializer = new MqttPublishMessageMaterializer();
        var value = FlowValue.From(new
        {
            topic = "orders/created",
            content = "payload",
            qos = 1,
            retain = true
        });

        var result = materializer.Materialize(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Topic.ShouldBe("orders/created");
        result.Value.Qos.ShouldBe(MqttQos.AtLeastOnce);
        result.Value.Retain.ShouldBeTrue();
        result.Value.Content.ContentType.ShouldBe("text/plain");
        Encoding.UTF8.GetString(result.Value.Content.Bytes.ToArray()).ShouldBe("payload");
    }

    [Fact]
    public void Materialize_rejects_a_non_object_with_mqtt_owned_error()
    {
        var result = new MqttPublishMessageMaterializer().Materialize(FlowValue.From(42));

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().Code.ShouldBe("mqtt.publish.invalid_shape");
        result.Error.Category.ShouldBe("MQTT");
    }
}
