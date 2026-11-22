using System.Text;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Serial;

public sealed class SerialReader : FlowNode, IFlowSource
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public SerialReader(SerialSession session, SerialFrameOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= new(); options.Validate();
        Output = new(static value => value, new() { BoundedCapacity = options.Capacity });
        Events = new(static value => value);
        Lifetime = new(this, produce: token => Task.Factory.StartNew(() => Receive(session, options, token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
        using var cancelStartup = cancellationToken.Register(StopProduction);
        await _ready.Task.WaitAsync(Stopping).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
    public void StopProduction() => Lifetime.StopProduction();
    private void Receive(SerialSession session, SerialFrameOptions options, CancellationToken token)
    {
        try
        {
            using var reader = session.ClaimReader();
            session.OpenAsync(token).GetAwaiter().GetResult();
            _ready.TrySetResult();
            var decoder = new SerialFrameDecoder(options);
            var encoding = options.GetEncoding();
            var buffer = new byte[options.ReadBufferBytes];
            while (!token.IsCancellationRequested)
            {
                int count;
                try { count = session.Read(buffer, token); }
                catch (TimeoutException) { continue; }
                foreach (var bytes in decoder.Push(buffer, count))
                {
                    token.ThrowIfCancellationRequested();
                    string? text = null, error = null;
                    try { text = encoding?.GetString(bytes); }
                    catch (DecoderFallbackException failure) { error = failure.Message; }
                    var frame = options.OutputFormat == SerialFrameOutputFormat.Bytes
                        ? JsonSerializer.SerializeToElement(new SerialBytesFrame(bytes, text, error), SerialFrameJsonContext.Default.SerialBytesFrame)
                        : JsonSerializer.SerializeToElement(new SerialFrame(Convert.ToBase64String(bytes), text, error), SerialFrameJsonContext.Default.SerialFrame);
                    EmitAsync(FlowMessage.Create(new JsonFlowValue(frame)), token)
                        .AsTask().GetAwaiter().GetResult();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { _ready.TrySetException(error); _ = _ready.Task.Exception; throw; }
        finally { _ready.TrySetCanceled(token); }
    }
}
