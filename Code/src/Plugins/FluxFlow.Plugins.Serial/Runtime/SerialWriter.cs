using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Serial;

public sealed class SerialWriter : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public SerialWriter(SerialSession session, SerialFrameOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= new(); options.Validate();
        var encoding = options.GetEncoding();
        var delimiter = options.Framing == SerialFraming.Delimiter ? Convert.FromBase64String(options.DelimiterBase64) : [];
        Output = new(static value => value, new() { BoundedCapacity = options.Capacity });
        Events = new(static value => value);
        Lifetime = new(this);
        var input = new ActionBlock<FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            var request = value.Value.Json.Deserialize(SerialFrameJsonContext.Default.SerialFrameWrite)
                ?? throw new ArgumentException("Expected Bytes, DataBase64 or Text.");
            var bytes = (request.Bytes, request.DataBase64, request.Text) switch
            {
                ({ } values, null, null) when values.Count <= options.MaxFrameBytes => values.ToArray(),
                (not null, null, null) => throw new ArgumentException("Serial write exceeds MaxFrameBytes."),
                (null, { } data, null) => Convert.FromBase64String(data),
                (null, null, { } text) => (encoding ?? throw new ArgumentException("Text requires a configured encoding.")).GetBytes(text),
                _ => throw new ArgumentException("Provide exactly one of Bytes, DataBase64 and Text.")
            };
            if (bytes.Length > options.MaxFrameBytes) throw new ArgumentException("Serial write exceeds MaxFrameBytes.");
            if (options.Framing == SerialFraming.FixedLength && bytes.Length != options.FrameLength)
                throw new ArgumentException("Serial write does not match FrameLength.");
            byte[] frame = delimiter.Length == 0 ? bytes : [.. bytes, .. delimiter];
            if (delimiter.Length > 0 && frame.AsSpan().IndexOf(delimiter) != bytes.Length)
                throw new ArgumentException("The delimiter would end the frame before the end of its payload.");
            try { await session.WriteAsync(frame, Stopping).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
            { Fault(error); throw; }
            // Completion means bytes were accepted by the driver, not that the device processed the command.
            await EmitAsync(value, Stopping).ConfigureAwait(false);
        }, Output, Stopping), new() { BoundedCapacity = options.Capacity, MaxDegreeOfParallelism = 1 });
        Input = input;
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
}
