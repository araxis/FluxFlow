using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.Client;

internal interface IMqttClientCommandOperations
{
    bool IsStarted { get; }

    ValueTask<MqttClientResult> ConnectAsync(CancellationToken cancellationToken);

    ValueTask<MqttClientResult> DisconnectAsync(
        string? reason,
        CancellationToken cancellationToken);

    MqttStatusResult CreateStatusResult();

    ValueTask<MqttClientResult> PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken);

    ValueTask<MqttClientResult> SubscribeAsync(
        MqttSubscribeRequest request,
        CancellationToken cancellationToken);

    ValueTask<MqttClientResult> UnsubscribeAsync(
        MqttUnsubscribeRequest request,
        CancellationToken cancellationToken);
}

internal sealed class MqttClientCommandDispatcher(
    IMqttClientCommandOperations operations,
    MqttClientResultFactory results)
{
    private readonly IMqttClientCommandOperations _operations =
        operations ?? throw new ArgumentNullException(nameof(operations));
    private readonly MqttClientResultFactory _results =
        results ?? throw new ArgumentNullException(nameof(results));

    internal async ValueTask<MqttClientResult> ExecuteAsync(
        MqttClientRequest request,
        CancellationToken cancellationToken)
    {
        if (!_operations.IsStarted)
        {
            return _results.Failure(
                request.Operation,
                MqttClientErrorCodes.NotStarted,
                "The MQTT client controller has not started.",
                isTransient: false);
        }

        try
        {
            return request switch
            {
                MqttConnectRequest => await _operations.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false),
                MqttDisconnectRequest disconnect => await _operations.DisconnectAsync(
                    disconnect.Reason,
                    cancellationToken).ConfigureAwait(false),
                MqttStatusRequest => _operations.CreateStatusResult(),
                MqttPublishClientRequest publish => await _operations.PublishAsync(
                    publish.Message,
                    cancellationToken).ConfigureAwait(false),
                MqttSubscribeRequest subscribe => await _operations.SubscribeAsync(
                    subscribe,
                    cancellationToken).ConfigureAwait(false),
                MqttUnsubscribeRequest unsubscribe => await _operations.UnsubscribeAsync(
                    unsubscribe,
                    cancellationToken).ConfigureAwait(false),
                _ => _results.Failure(
                    request.Operation,
                    MqttClientErrorCodes.InvalidRequest,
                    $"Unsupported MQTT client request '{request.GetType().Name}'.",
                    isTransient: false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttClientOperationException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return _results.Failure(
                request.Operation,
                MqttClientErrorCodes.InvalidRequest,
                exception.Message,
                isTransient: false,
                exception);
        }
        catch (MqttTransportException exception)
        {
            return _results.Failure(
                request.Operation,
                MqttClientResultFactory.ErrorCodeFor(request.Operation),
                exception.Message,
                exception.IsTransient,
                exception);
        }
        catch (Exception exception)
        {
            return _results.Failure(
                request.Operation,
                MqttClientResultFactory.ErrorCodeFor(request.Operation),
                exception.Message,
                isTransient: true,
                exception);
        }
    }
}
