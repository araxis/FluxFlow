using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxFlow.Engine.DurableInput;

/// <summary>
/// Persists application input messages for leased at-least-once delivery.
/// </summary>
public sealed class DurableApplicationInputs
{
    private readonly IDurableInputStore _store;
    private readonly DurableInputContractRegistry _contracts;
    private readonly TimeProvider _clock;
    private readonly ILogger<DurableApplicationInputs> _logger;

    internal DurableApplicationInputs(
        IDurableInputStore store,
        DurableInputContractRegistry contracts,
        TimeProvider clock,
        ILogger<DurableApplicationInputs>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<DurableApplicationInputs>.Instance;
    }

    public ValueTask<DurableInputEnqueueResult> EnqueueAsync<T>(
        string input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
        => EnqueueAsync(ApplicationAddress.Parse(input), message, cancellationToken);

    public ValueTask<DurableInputEnqueueResult> EnqueueAsync<T>(
        InputPortHandle<T> input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return EnqueueAsync(input.Address, message, cancellationToken);
    }

    public async ValueTask<DurableInputEnqueueResult> EnqueueAsync<T>(
        ApplicationAddress input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(message);
        if (input.Kind != ApplicationAddressKind.WorkflowPort)
            throw new ArgumentException("Durable input requires a workflow port address.", nameof(input));
        var contract = _contracts.GetByType(typeof(T));
        var envelope = contract.CreateEnvelope(input, message, _clock.GetUtcNow());
        DurableInputEnqueueResult result;
        try
        {
            result = await _store.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Durable input store enqueue failed for {MessageId} at {Address} using {ContractName}.",
                message.MessageId,
                input.Value,
                contract.Name);
            throw;
        }

        if (result is null || result.Key != envelope.Key)
        {
            var exception = new InvalidOperationException(
                "The durable input store returned an enqueue result for a different key.");
            _logger.LogError(
                exception,
                "Durable input store returned an invalid enqueue result for {MessageId} at {Address}.",
                message.MessageId,
                input.Value);
            throw exception;
        }

        switch (result.Status)
        {
            case DurableInputEnqueueStatus.Enqueued:
                _logger.LogInformation(
                    "Durably enqueued input {MessageId} at {Address} using {ContractName}.",
                    message.MessageId,
                    input.Value,
                    contract.Name);
                break;
            case DurableInputEnqueueStatus.AlreadyExists:
                _logger.LogDebug(
                    "Durable input {MessageId} at {Address} already exists for {ContractName}.",
                    message.MessageId,
                    input.Value,
                    contract.Name);
                break;
            case DurableInputEnqueueStatus.Conflict:
                _logger.LogWarning(
                    "Durable input enqueue conflict for {MessageId} at {Address} using {ContractName}.",
                    message.MessageId,
                    input.Value,
                    contract.Name);
                break;
            default:
                throw new InvalidOperationException($"Unknown durable enqueue status '{result.Status}'.");
        }

        return result;
    }
}
