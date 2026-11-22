using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Bluetooth;

public sealed class BleNotifications : FlowNode, IFlowSource
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BleNotifications(IBlePlatform platform, BleClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(platform);
        options.ValidateSubscription();
        Output = new(static value => value, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static value => value);
        Lifetime = new FlowLifetime(this, produce: async token =>
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var accepting = 1;
            try
            {
                await platform.SubscribeAsync(options, async bytes =>
                {
                    if (Volatile.Read(ref accepting) == 0 || startup.IsCancellationRequested) return;
                    using var activity = FlowTracing.StartReceiving(this);
                    try { await EmitAsync(FlowMessage.Create(new BleReadResult(Convert.ToBase64String(bytes), bytes.Length)), startup.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (startup.IsCancellationRequested) { activity.Canceled(); }
                    catch (Exception error) { activity.Failed(error); throw; }
                }, () =>
                {
                    if (Volatile.Read(ref accepting) == 0 || startup.IsCancellationRequested) return;
                    startup.CancelAfter(Timeout.InfiniteTimeSpan);
                    _ready.TrySetResult();
                }, startup.Token).ConfigureAwait(false);
                if (!token.IsCancellationRequested) throw new InvalidOperationException("BLE subscription stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                _ready.TrySetException(error);
                _ = _ready.Task.Exception;
                throw;
            }
            finally { Interlocked.Exchange(ref accepting, 0); _ready.TrySetCanceled(token); }
        });
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
            await _ready.Task.WaitAsync(Stopping).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Lifetime.StopProduction();
            await ProductionCompletion.ConfigureAwait(false);
            throw;
        }
    }
    public void StopProduction() => Lifetime.StopProduction();
}
