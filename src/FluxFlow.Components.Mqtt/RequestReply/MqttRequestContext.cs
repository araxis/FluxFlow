using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using System.Text;

namespace FluxFlow.Components.Mqtt.RequestReply;

/// <summary>
/// Bridges one inbound MQTT request to the request/reply bridge. The correlation id is
/// seeded from MQTT5 correlation data when present; <see cref="ReplyAsync"/> publishes
/// the graph's reply to the request's response topic, echoing the original correlation
/// data so the requester can match it. A request with no response topic is
/// fire-and-forget (the reply is dropped).
/// </summary>
public sealed class MqttRequestContext : IRequestContext<MqttRequest, MqttReply>
{
    private readonly IMqttResponsePublisher _publisher;

    public MqttRequestContext(MqttRequest request, IMqttResponsePublisher publisher)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        CorrelationId = SeedCorrelationId(request.CorrelationData);
    }

    public MqttRequest Request { get; }

    public CorrelationId? CorrelationId { get; }

    public async Task ReplyAsync(MqttReply reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (string.IsNullOrEmpty(Request.ResponseTopic))
        {
            // Fire-and-forget request: nowhere to reply.
            return;
        }

        await _publisher.PublishAsync(
            new MqttResponseMessage(
                Request.ResponseTopic,
                reply.Payload,
                Request.CorrelationData,
                reply.ContentType),
            cancellationToken).ConfigureAwait(false);
    }

    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        // Fire-and-forget over MQTT: the message is published into the graph and there is
        // nothing to send back to the requester.
        => Task.CompletedTask;

    public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        // MQTT has no standard error reply; the requester relies on its own timeout.
        => Task.CompletedTask;

    private static CorrelationId? SeedCorrelationId(byte[]? correlationData)
    {
        if (correlationData is not { Length: > 0 })
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(correlationData);
        return string.IsNullOrWhiteSpace(text) ? null : new CorrelationId(text);
    }
}
