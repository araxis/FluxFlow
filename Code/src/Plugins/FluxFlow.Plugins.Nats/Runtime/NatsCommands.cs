using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using NATS.Client.Core;

namespace FluxFlow.Components.Nats;

public abstract class NatsCommand : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    protected NatsCommand(NatsSession connection, NatsComponentOptions? options,
        Func<NatsConnection, NatsMessage, byte[], CancellationToken, Task<NatsMessage?>> execute)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new(); options.Validate();
        Output = new(static value => value, new() { BoundedCapacity = options.Capacity });
        Events = new(static value => value);
        Lifetime = new(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            var request = NatsMessage.Read(value);
            var bytes = request.Bytes(options.MaxMessageBytes);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stopping, connection.FailureToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            NatsMessage? response;
            try
            {
                connection.ThrowIfFailed();
                var native = await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
                response = await execute(native, request, bytes, timeout.Token).ConfigureAwait(false);
                connection.ThrowIfFailed();
            }
            catch (OperationCanceledException) { connection.ThrowIfFailed(); throw; }
            return new[] { response is null ? value : value.WithValue(response.Value()) }.AsEnumerable();
        }, Output, Stopping), new() { BoundedCapacity = options.Capacity, MaxDegreeOfParallelism = 1 });
        input.LinkTo(Output); Input = input;
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
}

public sealed class NatsPublisher(NatsSession connection, NatsComponentOptions? options = null)
    : NatsCommand(connection, options, async (native, request, bytes, token) =>
    {
        NatsComponentOptions.ValidateSubject(request.Subject);
        if (request.ReplyTo is not null) NatsComponentOptions.ValidateSubject(request.ReplyTo);
        await native.PublishAsync(request.Subject, bytes, request.NativeHeaders(), request.ReplyTo, cancellationToken: token).ConfigureAwait(false);
        return null;
    });

public sealed class NatsReply(NatsSession connection, NatsComponentOptions? options = null)
    : NatsCommand(connection, options, async (native, request, bytes, token) =>
    {
        NatsComponentOptions.ValidateSubject(request.ReplyTo!);
        await native.PublishAsync(request.ReplyTo!, bytes, request.NativeHeaders(), cancellationToken: token).ConfigureAwait(false);
        return null;
    });

public sealed class NatsRequest(NatsSession connection, NatsComponentOptions? options = null)
    : NatsCommand(connection, options, async (native, request, bytes, token) =>
    {
        NatsComponentOptions.ValidateSubject(request.Subject);
        if (request.ReplyTo is not null) throw new ArgumentException("Request owns its reply inbox. Do not supply ReplyTo.");
        var response = await native.RequestAsync<byte[], byte[]>(request.Subject, bytes, request.NativeHeaders(),
            replyOpts: new() { Timeout = TimeSpan.FromSeconds((options ?? new()).TimeoutSeconds), ThrowIfNoResponders = true }, cancellationToken: token).ConfigureAwait(false);
        return NatsMessage.FromNative(response, (options ?? new()).MaxMessageBytes);
    });
