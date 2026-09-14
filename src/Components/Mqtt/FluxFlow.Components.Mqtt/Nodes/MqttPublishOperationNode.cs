using System.Text.Json;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishOperationNode : FlowTransformNode
{
    private readonly IMqttClientController _controller;

    public MqttPublishOperationNode(
        IMqttClientController controller,
        int maximumPendingRequests = 128)
        : base(CreateNodeOptions(maximumPendingRequests))
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    protected override async Task<FlowMessage> TransformAsync(FlowMessage message)
    {
        MqttPublishMessage inputValue;
        try
        {
            inputValue = MqttPublishJson.Read(message.Value!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = new FlowError("mqtt.publish.invalid_shape", "The workflow value cannot be materialized as an MQTT publish request.", "MQTT",
                details: JsonSerializer.SerializeToElement(new { target = typeof(MqttPublishMessage).Name, reason = exception.Message }));
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.publish.rejected",
                Level = FlowEventLevel.Warning,
                Message = error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["errorCode"] = error.Code
                }
            });
            return message.WithError(error);
        }

        var request = inputValue;

        try
        {
            var result = await _controller.ExecuteAsync(new MqttPublishClientRequest
            {
                Message = request
            }).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = result.Timestamp,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.publish.completed",
                Level = FlowEventLevel.Information,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["topic"] = request.Topic,
                    ["qos"] = request.Qos.ToString(),
                    ["retain"] = request.Retain
                }
            });
            return message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(result, ValueJsonOptions)));
        }
        catch (MqttClientOperationException exception)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.publish.failed",
                Level = FlowEventLevel.Warning,
                Message = exception.Error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["topic"] = request.Topic,
                    ["qos"] = request.Qos.ToString(),
                    ["retain"] = request.Retain,
                    ["errorCode"] = exception.Error.Code
                }
            });
            return message.WithError(exception.Error);
        }
    }

    private static FlowNodeOptions CreateNodeOptions(int maximumPendingRequests)
    {
        if (maximumPendingRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
        }

        return new FlowNodeOptions
        {
            InputCapacity = maximumPendingRequests,
            OutputCapacity = maximumPendingRequests
        };
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
