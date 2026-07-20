using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Timers.Nodes;

internal static class FlowValueTimerNodeSupport
{
    public static FlowMessage<FlowResult<FlowValue>> Success(
        FlowMessage<FlowValue> message,
        string kind,
        DateTimeOffset timestamp)
        => message.With(FlowResult<FlowValue>.Success(kind, message.Payload, timestamp));

    public static FlowMessage<FlowResult<FlowValue>> Failure(
        FlowMessage<FlowValue> message,
        string kind,
        string code,
        string text,
        string nodeType,
        string? name,
        DateTimeOffset timestamp,
        Exception? exception = null,
        IReadOnlyDictionary<string, FlowValue>? timing = null)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["input"] = message.Payload ?? FlowValue.Null,
            ["name"] = Optional(name),
            ["nodeType"] = FlowValue.From(nodeType)
        };
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }
        if (timing is not null)
        {
            foreach (var item in timing)
                details[item.Key] = item.Value;
        }

        var error = new DataFlowError(
            code,
            text,
            category: "Timers",
            isTransient: exception is not null,
            details: FlowValue.FromObject(details));
        return message.With(FlowResult<FlowValue>.Failure(kind, error, timestamp));
    }

    public static FlowEvent Event(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        string nodeType,
        string? configuredName,
        string? errorCode,
        IReadOnlyDictionary<string, object?> timing)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["isError"] = errorCode is not null,
            ["name"] = configuredName,
            ["nodeType"] = nodeType,
            ["resultKind"] = resultKind
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;
        foreach (var item in timing)
            attributes[item.Key] = item.Value;

        return new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        };
    }

    private static FlowValue Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
