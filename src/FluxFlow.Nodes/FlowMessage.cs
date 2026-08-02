using System.Collections.Immutable;
using System.Text.Json.Serialization;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

/// <summary>
/// Immutable workflow envelope containing exactly one active case: a value of
/// <typeparamref name="T"/> or a <see cref="FlowError"/>.
/// </summary>
[JsonConverter(typeof(FlowMessageJsonConverterFactory))]
public sealed record FlowMessage<T>
{
    private readonly T? _value;
    private readonly FlowError? _error;

    private FlowMessage(
        bool isError,
        T? value,
        FlowError? error,
        CorrelationId? correlationId,
        TraceId traceId,
        MessageId messageId,
        MessageId? causationId,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (isError && error is null)
            throw new ArgumentException("An error message requires an error.", nameof(error));
        if (!isError && error is not null)
            throw new ArgumentException("A value message cannot contain an error.", nameof(error));
        if (traceId.IsEmpty)
            throw new ArgumentException("Trace id cannot be empty.", nameof(traceId));
        if (messageId.IsEmpty)
            throw new ArgumentException("Message id cannot be empty.", nameof(messageId));

        IsError = isError;
        _value = value;
        _error = error;
        CorrelationId = correlationId is { IsEmpty: false } ? correlationId : null;
        TraceId = traceId;
        MessageId = messageId;
        CausationId = causationId is { IsEmpty: false } ? causationId : null;
        Timestamp = timestamp;
        Headers = FlowMessage.CopyHeaders(headers);
    }

    public bool IsError { get; }

    public T Value => !IsError
        ? _value!
        : throw new InvalidOperationException("An error message does not contain a value.");

    public FlowError? Error => _error;

    public CorrelationId? CorrelationId { get; }

    public TraceId TraceId { get; }

    public MessageId MessageId { get; }

    public MessageId? CausationId { get; }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public TResult Match<TResult>(
        Func<T, TResult> onValue,
        Func<FlowError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(onError);
        return IsError ? onError(_error!) : onValue(_value!);
    }

    public FlowMessage<TNext> With<TNext>(
        TNext value,
        IReadOnlyDictionary<string, string>? headers = null,
        MessageId? causationId = null)
        => FlowMessage<TNext>.CreateDerived(this, value, error: null, headers, causationId);

    public FlowMessage<TNext> WithError<TNext>(
        FlowError error,
        IReadOnlyDictionary<string, string>? headers = null,
        MessageId? causationId = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FlowMessage<TNext>.CreateDerived(
            this,
            value: default,
            error,
            headers,
            causationId);
    }

    internal static FlowMessage<T> CreateInitial(
        T? value,
        FlowError? error,
        CorrelationId? correlationId,
        TraceId? traceId,
        IReadOnlyDictionary<string, string>? headers,
        MessageId? causationId)
        => new(
            error is not null,
            value,
            error,
            correlationId,
            traceId is { IsEmpty: false } ? traceId.Value : global::FluxFlow.Nodes.TraceId.New(),
            global::FluxFlow.Nodes.MessageId.New(),
            causationId is { IsEmpty: false } ? causationId : null,
            DateTimeOffset.UtcNow,
            headers);

    internal static FlowMessage<T> Rehydrate(
        bool isError,
        T? value,
        FlowError? error,
        CorrelationId? correlationId,
        TraceId traceId,
        MessageId messageId,
        MessageId? causationId,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, string>? headers)
        => new(
            isError,
            value,
            error,
            correlationId,
            traceId,
            messageId,
            causationId,
            timestamp,
            headers);

    private static FlowMessage<T> CreateDerived<TPrevious>(
        FlowMessage<TPrevious> previous,
        T? value,
        FlowError? error,
        IReadOnlyDictionary<string, string>? headers,
        MessageId? causationId)
        => new(
            error is not null,
            value,
            error,
            previous.CorrelationId,
            previous.TraceId,
            global::FluxFlow.Nodes.MessageId.New(),
            causationId is { IsEmpty: false } ? causationId : previous.MessageId,
            DateTimeOffset.UtcNow,
            headers ?? previous.Headers);
}

public static class FlowMessage
{
    internal static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public static FlowMessage<T> Create<T>(
        T value,
        CorrelationId? correlationId = null,
        TraceId? traceId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        MessageId? causationId = null)
        => FlowMessage<T>.CreateInitial(
            value,
            error: null,
            correlationId,
            traceId,
            headers,
            causationId);

    public static FlowMessage<T> CreateError<T>(
        FlowError error,
        CorrelationId? correlationId = null,
        TraceId? traceId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        MessageId? causationId = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FlowMessage<T>.CreateInitial(
            value: default,
            error,
            correlationId,
            traceId,
            headers,
            causationId);
    }

    /// <summary>
    /// Restores a previously created value message without generating new identity or timing metadata.
    /// </summary>
    public static FlowMessage<T> Restore<T>(
        T value,
        MessageId messageId,
        TraceId traceId,
        DateTimeOffset timestamp,
        CorrelationId? correlationId = null,
        MessageId? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => FlowMessage<T>.Rehydrate(
            isError: false,
            value,
            error: null,
            correlationId,
            traceId,
            messageId,
            causationId,
            timestamp,
            headers);

    /// <summary>
    /// Restores a previously created error message without generating new identity or timing metadata.
    /// </summary>
    public static FlowMessage<T> RestoreError<T>(
        FlowError error,
        MessageId messageId,
        TraceId traceId,
        DateTimeOffset timestamp,
        CorrelationId? correlationId = null,
        MessageId? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FlowMessage<T>.Rehydrate(
            isError: true,
            value: default,
            error,
            correlationId,
            traceId,
            messageId,
            causationId,
            timestamp,
            headers);
    }

    internal static IReadOnlyDictionary<string, string> CopyHeaders(
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return EmptyHeaders;
        if (headers is ImmutableDictionary<string, string> immutable &&
            immutable.KeyComparer == StringComparer.Ordinal)
        {
            return immutable;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(header.Key);
            builder.Add(
                header.Key,
                header.Value ?? throw new ArgumentException(
                    "FlowMessage headers cannot contain null values.",
                    nameof(headers)));
        }

        return builder.ToImmutable();
    }
}
