using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

/// <summary>A typed view of the envelope. Common ports carry FlowMessage.</summary>
[JsonConverter(typeof(FlowMessageJsonConverterFactory))]
public sealed record FlowMessage<T> : FlowMessage where T : FlowValue
{
    internal FlowMessage(FlowMessage message, T? value = null)
        : base(message, value ?? message.Value) { }

    internal FlowMessage(T value, CorrelationId? correlationId, TraceId? traceId,
        IReadOnlyDictionary<string, FlowValue>? headers, MessageId? causationId)
        : base(value, correlationId, traceId, headers, causationId) { }

    internal FlowMessage(T value, FlowMessage source,
        IReadOnlyDictionary<string, FlowValue>? headers, MessageId? causationId)
        : base(value, source, headers, causationId) { }

    protected override bool PrintMembers(System.Text.StringBuilder builder) => base.PrintMembers(builder);

    public new T Value => base.Value is T value ? value
        : throw new InvalidOperationException("An error message does not contain the requested typed value.");

    public TResult Match<TResult>(Func<T, TResult> onValue, Func<FlowError, TResult> onError)
        => FlowMessageExtensions.Match(this, onValue, onError);
}
