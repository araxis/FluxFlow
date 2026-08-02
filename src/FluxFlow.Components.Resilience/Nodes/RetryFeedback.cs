using FluxFlow.Nodes;

namespace FluxFlow.Components.Resilience.Nodes;

internal enum RetryFeedbackKind
{
    Ack = 0,
    Nak = 1,
    Cancel = 2
}

internal sealed record RetryFeedback(RetryFeedbackKind Kind, MessageId MessageId);
