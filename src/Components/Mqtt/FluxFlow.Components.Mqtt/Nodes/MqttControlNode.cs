using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Materialization;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttControlNode : FlowTransformNode
{
    private readonly IMqttClientController _controller;
    private readonly IFlowValueMaterializer<MqttClientRequest> _materializer;

    public MqttControlNode(
        IMqttClientController controller,
        MqttControlOptions? options = null,
        IFlowValueMaterializer<MqttClientRequest>? materializer = null)
        : base(ProcessingOptions(controller, options), options?.ResultOrder != MqttResultOrder.Completion)
    {
        _controller = controller;
        _materializer = materializer ?? new MqttClientRequestMaterializer();
    }

    protected override async Task<FlowMessage> TransformAsync(
        FlowMessage message)
    {
        var materialized = _materializer.Materialize(message.Value!);
        if (!materialized.IsSuccess)
        {
            var error = materialized.Error!;
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
            var result = await _controller.ExecuteAsync(materialized.Value, Stopping).ConfigureAwait(false);
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
            return message.With(FlowValue.From<MqttClientResult>(result));
        }
        catch (MqttClientOperationException exception)
        {
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
}
