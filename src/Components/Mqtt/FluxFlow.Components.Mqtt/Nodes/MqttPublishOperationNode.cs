using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Materialization;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishOperationNode : FlowNode
{
    private readonly IMqttClientController _controller;
    private readonly IFlowValueMaterializer<MqttPublishMessage> _materializer;

    public MqttPublishOperationNode(
        IMqttClientController controller,
        int maximumPendingRequests = 128,
        IFlowValueMaterializer<MqttPublishMessage>? materializer = null)
        : base(CreateNodeOptions(maximumPendingRequests))
    {
        if (maximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _materializer = materializer ?? new MqttPublishMessageMaterializer();
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var result = await ProcessCoreAsync(message).ConfigureAwait(false);
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private async Task<FlowMessage> ProcessCoreAsync(
        FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message;

        var materialized = _materializer.Materialize(message.Value);
        if (!materialized.IsSuccess)
        {
            var error = materialized.Error!;
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

        var request = materialized.Value;

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
            return message.With(FlowValue.From<MqttClientResult>(result));
        }
        catch (MqttClientOperationException exception)
        {
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
}
