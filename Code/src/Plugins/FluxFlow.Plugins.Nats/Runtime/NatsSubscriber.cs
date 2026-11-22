using System.Threading.Channels;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using NATS.Client.Core;

namespace FluxFlow.Components.Nats;

public sealed class NatsSubscriber : FlowNode, IFlowSource
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public NatsSubscriber(NatsSession connection, string subject, string? queueGroup = null, NatsComponentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new(); options.Validate();
        NatsComponentOptions.ValidateSubject(subject, true);
        if (queueGroup is not null) NatsComponentOptions.ValidateSubject(queueGroup);
        Output = new(static value => value, new() { BoundedCapacity = options.Capacity });
        Events = new(static value => value);
        Lifetime = new(this, produce: async token =>
        {
            using var active = CancellationTokenSource.CreateLinkedTokenSource(token, connection.FailureToken);
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                var native = await connection.OpenAsync(active.Token).ConfigureAwait(false);
                await using var subscription = await native.SubscribeCoreAsync<byte[]>(subject, queueGroup,
                    opts: new() { ChannelOpts = new() { Capacity = options.Capacity, FullMode = BoundedChannelFullMode.Wait } }, cancellationToken: active.Token).ConfigureAwait(false);
                // A round trip confirms that the server has processed the subscription before startup completes.
                await native.PingAsync(active.Token).ConfigureAwait(false);
                active.CancelAfter(Timeout.InfiniteTimeSpan);
                active.Token.ThrowIfCancellationRequested();
                if (subscription is NatsSubBase { Exception: { } startupError }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupError).Throw();
                _ready.TrySetResult();
                await foreach (var message in subscription.Msgs.ReadAllAsync(active.Token).ConfigureAwait(false))
                {
                    active.Token.ThrowIfCancellationRequested();
                    using var activity = FlowTracing.StartReceiving(this);
                    try { await EmitAsync(FlowMessage.Create(NatsMessage.FromNative(message, options.MaxMessageBytes).Value()), active.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { activity.Canceled(); throw; }
                    catch (Exception error) { activity.Failed(error); throw; }
                }
                token.ThrowIfCancellationRequested();
                if (subscription is NatsSubBase { Exception: { } subscriptionError }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(subscriptionError).Throw();
                throw new IOException("The NATS subscription ended before Stop.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                try { connection.ThrowIfFailed(); }
                catch (Exception failure) { error = failure; }
                _ready.TrySetException(error); _ = _ready.Task.Exception;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
            finally { _ready.TrySetCanceled(token); }
        });
        Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
            await _ready.Task.WaitAsync(Stopping).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StopProduction(); await ProductionCompletion.ConfigureAwait(false); throw;
        }
    }
    public void StopProduction() => Lifetime.StopProduction();
}
