using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.RateLimit;

[JsonConverter(typeof(JsonStringEnumConverter<RequestReplyOutcome>))]
public enum RequestReplyOutcome { ACK, NACK, Timeout, NotDispatched }

/// <summary>An application-level completion outcome, independent of the reply payload's type.</summary>
public sealed class RequestReplyResult : FlowValue
{
    internal RequestReplyResult(RequestReplyOutcome outcome, FlowMessage request, FlowValue? reply, TimeSpan elapsed)
    {
        Outcome = outcome;
        RequestId = request.MessageId.ToString();
        Request = request.Value;
        Reply = reply;
        Elapsed = elapsed;
    }

    public RequestReplyOutcome Outcome { get; }
    public string RequestId { get; }
    public FlowValue Request { get; }
    public FlowValue? Reply { get; }
    public TimeSpan Elapsed { get; }

    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, RequestReplyJsonContext.Default.RequestReplyResult);
}

[JsonSerializable(typeof(RequestReplyResult))]
internal partial class RequestReplyJsonContext : JsonSerializerContext;
