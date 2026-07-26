using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerNodeSupport
{
    public static FlowMessage<T> Success<T>(FlowMessage<T> message)
        => message.With(message.Value);

    public static FlowMessage<T> Failure<T>(
        FlowMessage<T> message,
        string code,
        string text,
        string nodeType,
        string? name,
        DateTimeOffset timestamp,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? timing = null)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = Optional(name),
            ["nodeType"] = nodeType
        };
        if (exception is not null)
        {
            details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
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
            details: JsonSerializer.SerializeToElement(details));
        return message.WithError<T>(error);
    }

    public static FlowEvent Event<T>(
        FlowMessage<T> message,
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

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
