using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components.RequestReply;

public enum RequestReplyTimeoutBehavior { StopDispatch, Continue }

public sealed record RequestReplyConfiguration : ComponentConfiguration
{
    public int MaxInFlight { get; set; } = 1;
    public int BufferCapacity { get; set; } = 128;
    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public RequestReplyTimeoutBehavior TimeoutBehavior { get; set; } = RequestReplyTimeoutBehavior.StopDispatch;
    public OneOrManyPortReferences Request { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences ACK { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences NACK { get; init; } = OneOrManyPortReferences.Empty;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInFlight);
        ArgumentOutOfRangeException.ThrowIfNegative(BufferCapacity);
        if (BufferCapacity > int.MaxValue - MaxInFlight)
            throw new ArgumentOutOfRangeException(nameof(BufferCapacity));
        if (ReplyTimeout <= TimeSpan.Zero || ReplyTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(ReplyTimeout));
        if (!Enum.IsDefined(TimeoutBehavior)) throw new ArgumentOutOfRangeException(nameof(TimeoutBehavior));
    }
}
