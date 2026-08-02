using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishOperationNode : FlowNode<MqttPublishMessage, MqttClientResult>
{
    private readonly IMqttClientController _controller;
    public MqttPublishOperationNode(
        IMqttClientController controller,
        int maximumPendingRequests = 128)
        : base(CreateNodeOptions(maximumPendingRequests))
    {
        if (maximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<MqttPublishMessage> message)
    {
        var result = await ProcessCoreAsync(message).ConfigureAwait(false);
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private async Task<FlowMessage<MqttClientResult>> ProcessCoreAsync(
        FlowMessage<MqttPublishMessage> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<MqttClientResult>(message.Error!);

        try
        {
            var result = await _controller.ExecuteAsync(new MqttPublishClientRequest
            {
                Message = message.Value
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
                    ["topic"] = message.Value.Topic,
                    ["qos"] = message.Value.Qos.ToString(),
                    ["retain"] = message.Value.Retain
                }
            });
            return message.With(result);
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
                    ["topic"] = message.Value.Topic,
                    ["qos"] = message.Value.Qos.ToString(),
                    ["retain"] = message.Value.Retain,
                    ["errorCode"] = exception.Error.Code
                }
            });
            return message.WithError<MqttClientResult>(exception.Error);
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
