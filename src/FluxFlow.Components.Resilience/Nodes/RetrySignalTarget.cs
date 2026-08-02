using FluxFlow.Nodes;

namespace FluxFlow.Components.Resilience.Nodes;

internal sealed class RetrySignalTarget(
    Func<TraceId, IReadOnlyDictionary<string, string>, RetryFeedback, CancellationToken, ValueTask<bool>> send,
    Task completion,
    RetryFeedbackKind feedback) : IFlowSignalTarget
{
    public Task Completion => completion;

    public ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return send(
            signal.TraceId,
            signal.Headers,
            new RetryFeedback(feedback, signal.MessageId),
            cancellationToken);
    }
}
