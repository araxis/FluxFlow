using System.Diagnostics.CodeAnalysis;

namespace FluxFlow.Core.Messaging;

/// <summary>Convenience operations over the message's public envelope.</summary>
public static class FlowMessageExtensions
{
    public static bool TryGetValue(this FlowMessage message, [NotNullWhen(true)] out FlowValue? value)
    {
        ArgumentNullException.ThrowIfNull(message);
        value = message.IsError ? null : message.Value;
        return !message.IsError;
    }

    public static bool TryGetError(this FlowMessage message, [NotNullWhen(true)] out FlowError? error)
    {
        ArgumentNullException.ThrowIfNull(message);
        error = message.Error;
        return message.IsError;
    }

    public static TResult Match<TResult>(this FlowMessage message,
        Func<FlowValue, TResult> onValue, Func<FlowError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(onError);
        return message.IsError ? onError(message.Error!) : onValue(message.Value);
    }

    public static TResult Match<T, TResult>(this FlowMessage<T> message,
        Func<T, TResult> onValue, Func<FlowError, TResult> onError) where T : FlowValue
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(onError);
        return message.IsError ? onError(message.Error!) : onValue(message.Value);
    }

}
