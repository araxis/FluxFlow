using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableInput;

internal sealed class DurableInputContractRegistry
{
    private readonly IReadOnlyDictionary<string, IDurableInputContract> _byName;
    private readonly IReadOnlyDictionary<Type, IDurableInputContract> _byType;

    public DurableInputContractRegistry(IEnumerable<IDurableInputContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var byName = new Dictionary<string, IDurableInputContract>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, IDurableInputContract>();

        foreach (var contract in contracts)
        {
            if (!byName.TryAdd(contract.Name, contract))
                throw new InvalidOperationException(
                    $"Durable input contract name '{contract.Name}' is registered more than once.");
            if (!byType.TryAdd(contract.PayloadType, contract))
                throw new InvalidOperationException(
                    $"Durable input payload type '{contract.PayloadType}' is registered more than once.");
        }

        _byName = byName;
        _byType = byType;
    }

    public IDurableInputContract GetByType(Type payloadType)
        => _byType.TryGetValue(payloadType, out var contract)
            ? contract
            : throw new InvalidOperationException(
                $"Payload type '{payloadType}' has no durable input contract registration.");

    public bool TryGetByName(string name, out IDurableInputContract? contract)
        => _byName.TryGetValue(name, out contract);
}

internal interface IDurableInputContract
{
    string Name { get; }

    Type PayloadType { get; }

    bool IsEquivalentTo(IDurableInputContract other);

    DurableInputEnvelope CreateEnvelope<TMessage>(
        FluxFlow.Composition.Addressing.ApplicationAddress address,
        FlowMessage<TMessage> message,
        DateTimeOffset enqueuedAt);

    ValueTask<PortSendResult> RestoreAndSendAsync(
        FluxFlowApplication application,
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken);
}

internal sealed class DurableInputContract<T> : IDurableInputContract
{
    private readonly JsonTypeInfo<T>? _jsonTypeInfo;

    public DurableInputContract(string name, JsonTypeInfo<T>? jsonTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Contract name cannot have surrounding whitespace.", nameof(name));
        Name = name;
        _jsonTypeInfo = jsonTypeInfo;
    }

    public string Name { get; }

    public Type PayloadType => typeof(T);

    public bool IsEquivalentTo(IDurableInputContract other)
        => other is DurableInputContract<T> typed &&
           string.Equals(Name, typed.Name, StringComparison.Ordinal) &&
           ReferenceEquals(_jsonTypeInfo, typed._jsonTypeInfo);

    public DurableInputEnvelope CreateEnvelope<TMessage>(
        FluxFlow.Composition.Addressing.ApplicationAddress address,
        FlowMessage<TMessage> message,
        DateTimeOffset enqueuedAt)
    {
        if (typeof(TMessage) != typeof(T))
            throw new InvalidOperationException("The durable input contract payload type does not match the message.");

        var typedMessage = (FlowMessage<T>)(object)message;
        var payload = typedMessage.IsError
            ? JsonSerializer.SerializeToElement<object?>(null)
            : Serialize(typedMessage.Value);

        return new DurableInputEnvelope(
            address,
            Name,
            typedMessage.IsError,
            payload,
            typedMessage.Error,
            typedMessage.MessageId,
            typedMessage.TraceId,
            typedMessage.Timestamp,
            enqueuedAt,
            typedMessage.CorrelationId,
            typedMessage.CausationId,
            typedMessage.Headers);
    }

    public async ValueTask<PortSendResult> RestoreAndSendAsync(
        FluxFlowApplication application,
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var message = envelope.IsError
            ? FlowMessage.RestoreError<T>(
                envelope.Error!,
                envelope.MessageId,
                envelope.TraceId,
                envelope.Timestamp,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.Headers)
            : FlowMessage.Restore(
                Deserialize(envelope.Payload),
                envelope.MessageId,
                envelope.TraceId,
                envelope.Timestamp,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.Headers);

        return await application.Ports
            .SendAsync(envelope.Address, message, cancellationToken)
            .ConfigureAwait(false);
    }

    private JsonElement Serialize(T value)
        => _jsonTypeInfo is null
            ? JsonSerializer.SerializeToElement(value)
            : JsonSerializer.SerializeToElement(value, _jsonTypeInfo);

    private T Deserialize(JsonElement payload)
        => _jsonTypeInfo is null
            ? payload.Deserialize<T>()!
            : payload.Deserialize(_jsonTypeInfo)!;
}
