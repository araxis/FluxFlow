using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Bluetooth;

public sealed class BleGattServer : FlowNode, IFlowSource
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ITargetBlock<FlowMessage> Input { get; }
    public BleGattServer(IBlePlatform platform, BleServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(platform);
        options.Validate();
        options = options.Snapshot();
        Output = new(static value => value, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static value => value);
        BleServerSession? session = null;
        Lifetime = new FlowLifetime(this, produce: async token =>
        {
            session = new(options, async value =>
            {
                token.ThrowIfCancellationRequested();
                using var activity = FlowTracing.StartReceiving(this);
                try { await EmitAsync(FlowMessage.Create(value), token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { activity.Canceled(); }
                catch (Exception error) { activity.Failed(error); throw; }
            }, token);
            try
            {
                await platform.ServeAsync(options, session, () =>
                {
                    if (token.IsCancellationRequested || !_ready.TrySetResult()) return;
                    EmitEvent(new FlowEvent { Name = "ble.advertising_started", Kind = FlowEventKind.Lifecycle });
                }, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                    throw new InvalidOperationException("BLE GATT server stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                _ready.TrySetException(error);
                _ = _ready.Task.Exception;
                throw;
            }
            finally { session.Close(); _ready.TrySetCanceled(token); }
        });
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            await _ready.Task.WaitAsync(Stopping).ConfigureAwait(false);
            var result = await session!.UpdateAsync(BleServerUpdate.FromJson(value.Value.Json)).ConfigureAwait(false);
            return new[] { value.WithValue(result) }.AsEnumerable();
        }, Output, Stopping), new() { BoundedCapacity = options.OutputCapacity });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
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
