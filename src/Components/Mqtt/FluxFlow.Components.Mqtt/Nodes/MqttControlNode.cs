using System.Text.Json;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttControlNode : FlowTransformNode
{
    private readonly IMqttClientController _controller;

    public MqttControlNode(
        IMqttClientController controller,
        MqttControlOptions? options = null)
        : base(ProcessingOptions(controller, options), options?.ResultOrder != MqttResultOrder.Completion)
    {
        _controller = controller;
    }

    protected override async Task<FlowMessage> TransformAsync(
        FlowMessage message)
    {
        MqttClientRequest inputValue;
        try
        {
            inputValue = message.Value!.Json.Deserialize<MqttClientRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException("MqttClientRequest cannot be null.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = new FlowError("mqtt.command.invalid_shape", "The workflow value cannot be materialized as MqttClientRequest.", "MQTT",
                details: JsonSerializer.SerializeToElement(new { target = typeof(MqttClientRequest).Name, reason = exception.Message }));
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.command.rejected",
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

        try
        {
            var result = await _controller.ExecuteAsync(inputValue, Stopping).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = result.Timestamp,
                CorrelationId = message.CorrelationId,
                Name = "mqtt.command.completed",
                Level = FlowEventLevel.Information,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["operation"] = result.Operation.ToString(),
                    ["kind"] = result.Kind
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
                Name = "mqtt.command.failed",
                Level = FlowEventLevel.Warning,
                Message = exception.Error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["operation"] = exception.Operation.ToString(),
                    ["errorCode"] = exception.Error.Code
                }
            });
            return message.WithError(exception.Error);
        }
    }

    private static FlowNodeOptions ProcessingOptions(IMqttClientController controller, MqttControlOptions? options)
    {
        ArgumentNullException.ThrowIfNull(controller);
        options ??= new MqttControlOptions();
        if (!Enum.IsDefined(options.RequestProcessing))
            throw new ArgumentOutOfRangeException(nameof(options.RequestProcessing));
        if (!Enum.IsDefined(options.ResultOrder))
            throw new ArgumentOutOfRangeException(nameof(options.ResultOrder));
        if (options.MaximumConcurrentRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumConcurrentRequests));
        if (options.MaximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPendingRequests));
        return new FlowNodeOptions
        {
            InputCapacity = options.MaximumPendingRequests,
            OutputCapacity = options.MaximumPendingRequests,
            MaxDegreeOfParallelism = options.RequestProcessing == MqttRequestProcessing.Sequential
                ? 1 : options.MaximumConcurrentRequests
        };
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
