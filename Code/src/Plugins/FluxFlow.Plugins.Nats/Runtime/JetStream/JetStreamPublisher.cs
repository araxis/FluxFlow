using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using NATS.Client.JetStream;

namespace FluxFlow.Components.Nats.JetStream;

public sealed class JetStreamPublisher : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public JetStreamPublisher(NatsSession connection, JetStreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection); options.Validate();
        INatsJSContext? context = null;
        Output = new(static value => value, new() { BoundedCapacity = options.Limits.Capacity });
        Events = new(static value => value); Lifetime = new(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            var request = NatsMessage.Read(value); NatsComponentOptions.ValidateSubject(request.Subject);
            if (request.ReplyTo is not null) throw new ArgumentException("JetStream publishing owns the acknowledgement inbox; do not supply ReplyTo.");
            var bytes = request.Bytes(options.Limits.MaxMessageBytes);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stopping, connection.FailureToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.Limits.TimeoutSeconds));
            try
            {
                connection.ThrowIfFailed();
                if (context is null)
                {
                    var native = await connection.OpenAsync(deadline.Token).ConfigureAwait(false);
                    var candidate = new NatsJSContext(native);
                    await options.ProvisionAsync(candidate, deadline.Token).ConfigureAwait(false);
                    context = candidate;
                }
                var ack = await context.PublishAsync(request.Subject, bytes, opts: new() { ExpectedStream = options.Stream, RetryAttempts = 1 },
                    headers: request.NativeHeaders(), cancellationToken: deadline.Token).ConfigureAwait(false);
                // Duplicate acknowledgements are an explicit publish result, not a new stored message.
                if (ack.Error is not null) throw new NatsJSApiException(ack.Error);
                if (ack.Stream != options.Stream || ack.Seq == 0)
                    throw new InvalidDataException($"Expected a JetStream publish acknowledgement for '{options.Stream}' with a nonzero sequence; received stream '{ack.Stream}', sequence {ack.Seq}.");
                connection.ThrowIfFailed();
                return new[] { value.WithValue(JetStreamJson.Value(new JetStreamPublished(request, ack.Stream, ack.Seq, ack.Duplicate), JetStreamJson.Default.JetStreamPublished)) }.AsEnumerable();
            }
            catch (OperationCanceledException) { connection.ThrowIfFailed(); throw; }
        }, Output, Stopping), new() { BoundedCapacity = options.Limits.Capacity, MaxDegreeOfParallelism = 1 });
        input.LinkTo(Output); Input = input;
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
}
