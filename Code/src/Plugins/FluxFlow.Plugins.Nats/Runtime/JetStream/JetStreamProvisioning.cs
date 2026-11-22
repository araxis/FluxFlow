using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace FluxFlow.Components.Nats.JetStream;

internal static class JetStreamProvisioning
{
    internal static async Task ProvisionAsync(this JetStreamOptions options, INatsJSContext context, CancellationToken token)
    {
        if (!options.CreateStream) return;
        try { await context.GetStreamAsync(options.Stream, cancellationToken: token).ConfigureAwait(false); return; }
        catch (NatsJSApiException error) when (error.Error.ErrCode == 10059) { }
        // Create only: never update, purge or delete an externally owned stream.
        await context.CreateStreamAsync(new StreamConfig(options.Stream, [options.StreamSubject])
        {
            Storage = (StreamConfigStorage)options.Storage, MaxMsgs = options.MaxStreamMessages, MaxBytes = options.MaxStreamBytes,
            MaxAge = TimeSpan.FromSeconds(options.MaxStreamAgeSeconds), NumReplicas = 1
        }, token).ConfigureAwait(false);
    }

    internal static async Task<INatsJSConsumer> OpenAsync(this JetStreamConsumerOptions options, INatsJSContext context, CancellationToken token)
    {
        await options.Stream.ProvisionAsync(context, token).ConfigureAwait(false);
        INatsJSConsumer consumer;
        try { consumer = await context.GetConsumerAsync(options.Stream.Stream, options.Consumer, token).ConfigureAwait(false); }
        catch (NatsJSApiException error) when (options.CreateConsumer && error.Error.ErrCode == 10014)
        {
            consumer = await context.CreateConsumerAsync(options.Stream.Stream, new ConsumerConfig(options.Consumer)
            {
                AckPolicy = ConsumerConfigAckPolicy.Explicit, AckWait = TimeSpan.FromSeconds(options.AckWaitSeconds),
                MaxDeliver = options.MaxDeliver, MaxAckPending = options.MaxInFlight, FilterSubject = options.FilterSubject,
                DeliverPolicy = (ConsumerConfigDeliverPolicy)options.DeliverPolicy
            }, token).ConfigureAwait(false);
        }
        var config = consumer.Info.Config;
        if (config.AckPolicy != ConsumerConfigAckPolicy.Explicit || !string.IsNullOrEmpty(config.DeliverSubject) ||
            string.IsNullOrEmpty(config.DurableName))
            throw new ArgumentException("Select a durable pull consumer with explicit acknowledgements.");
        return consumer;
    }
}
