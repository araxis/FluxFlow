using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

internal sealed class MqttSignalTarget(
    Func<TraceId, MqttWorkflowOutcome, CancellationToken, ValueTask<bool>> send,
    Task completion,
    MqttWorkflowOutcome outcome) : IFlowSignalTarget
{
    public Task Completion { get; } = completion;

    public ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return send(signal.TraceId, outcome, cancellationToken);
    }
}
