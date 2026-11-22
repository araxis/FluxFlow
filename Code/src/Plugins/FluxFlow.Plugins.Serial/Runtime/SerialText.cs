using System.IO.Ports;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Serial;

/// <summary>One duplex text session. The caller or DI owns the supplied port's disposal.</summary>
public sealed class SerialText : FlowNode, IFlowSource
{
    private readonly SerialTextOptions _options;
    private readonly Action _open;
    private readonly Func<byte[], int> _read;
    private readonly Action<byte[]> _write;
    private readonly Action _close;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _opened;
    private readonly string? _portName;
    private SerialPortLease? _lease;

    public SerialText(SerialPort port, SerialTextOptions? options = null)
        : this(options ?? new(), OpenPort(port), buffer => port.Read(buffer, 0, buffer.Length),
            bytes => port.Write(bytes, 0, bytes.Length), port.Close) { _portName = port.PortName; }

    // Native I/O delegates keep the same lifecycle testable without a physical serial driver.
    internal SerialText(SerialTextOptions options, Action open, Func<byte[], int> read,
        Action<byte[]> write, Action close)
    {
        options.Validate();
        _options = options;
        _open = open;
        _read = read;
        _write = write;
        _close = close;
        Output = new(static value => value, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static value => value);
        Lifetime = new FlowLifetime(this, flush: CloseAsync, produce: token =>
            Task.Factory.StartNew(() => Receive(token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default));
        var input = new ActionBlock<FlowMessage>(message =>
            message.ProcessMessageAsync(this, WriteAsync, Output, Lifetime.ControlStopping),
            new() { BoundedCapacity = options.InputCapacity, MaxDegreeOfParallelism = 1 });
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public ITargetBlock<FlowMessage> Input { get; }
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
        using var cancelStartup = cancellationToken.Register(StopProduction);
        await _ready.Task.WaitAsync(Stopping).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void StopProduction()
    {
        Input.Complete();
        Lifetime.StopProduction();
    }

    private static Action OpenPort(SerialPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (port.IsOpen) throw new ArgumentException("SerialText requires an unopened port.", nameof(port));
        if (port.ReadTimeout <= 0 || port.WriteTimeout <= 0)
            throw new ArgumentException("SerialText requires finite positive read and write timeouts.", nameof(port));
        return port.Open;
    }

    private void Receive(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (_portName is not null) _lease = SerialPortLease.Acquire(_portName);
            _open();
            Interlocked.Exchange(ref _opened, 1);
            token.ThrowIfCancellationRequested();
            EmitEvent(new FlowEvent { Name = "serial.connected", Kind = FlowEventKind.Lifecycle });
            _ready.TrySetResult();
            var reader = new SerialLineReader(_options.MaxLineBytes);
            var buffer = new byte[1024];
            while (!token.IsCancellationRequested)
            {
                int count;
                try { count = _read(buffer); }
                catch (TimeoutException) { continue; }
                if (count == 0) throw new EndOfStreamException("The serial port stopped receiving data.");
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.Push(buffer[index]) is { } value)
                        EmitAsync(FlowMessage.Create(value), token).AsTask().GetAwaiter().GetResult();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _ready.TrySetException(error);
            _ = _ready.Task.Exception;
            EmitEvent(new FlowEvent
            {
                Name = "serial.connection_failed", Level = FlowEventLevel.Error,
                Kind = FlowEventKind.Lifecycle, Message = error.Message, Details = error.ToString()
            });
            throw;
        }
        finally { _ready.TrySetCanceled(token); }
    }

    private async Task WriteAsync(FlowMessage message)
    {
        var request = message.Value.Json.Deserialize(SerialJsonContext.Default.SerialWriteRequest)
            ?? throw new ArgumentException("Expected a SerialWriteRequest with Text.");
        ArgumentNullException.ThrowIfNull(request.Text);
        if (request.Text.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Text must contain one line without CR or LF.");
        if (SerialLineReader.Utf8.GetByteCount(request.Text) > _options.MaxLineBytes)
            throw new ArgumentException("Text exceeds MaxLineBytes.");
        if (!_ready.Task.IsCompletedSuccessfully)
            await _ready.Task.WaitAsync(Stopping).ConfigureAwait(false);
        Lifetime.ControlStopping.ThrowIfCancellationRequested();
        try { _write(SerialLineReader.Utf8.GetBytes(request.Text + _options.NewLine)); }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        {
            // A partial write cannot be retried safely. Fault instead of sending the next command.
            Fault(error);
            throw;
        }
        EmitEvent(message.WithValue(new FlowEvent { Name = "serial.written", Kind = FlowEventKind.Domain }));
    }

    private ValueTask CloseAsync()
    {
        if (Interlocked.Exchange(ref _opened, 0) != 0)
        {
            try { _close(); }
            catch { Interlocked.Exchange(ref _opened, 1); throw; }
            EmitEvent(new FlowEvent { Name = "serial.disconnected", Kind = FlowEventKind.Lifecycle });
        }
        _lease?.Dispose(); _lease = null;
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync() => CloseAsync();
}
