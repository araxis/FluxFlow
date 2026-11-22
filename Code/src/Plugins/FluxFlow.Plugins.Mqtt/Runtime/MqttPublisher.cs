using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using MQTTnet;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt;

public sealed class MqttPublisher : FlowNode
{
    private readonly MqttClientResource _connection;
    public ITargetBlock<FlowMessage> Input { get; }

    public MqttPublisher(MqttPublisherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        _connection = options.Connection;
        Output = new(static message => message, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, PublishAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> PublishAsync(FlowMessage message)
    {
        var request = MqttJson.Read<MqttPublishRequest>(message);
        MqttTopicValidator.ThrowIfInvalid(request.Topic);
        new MqttTopic { Topic = request.Topic, QoS = request.QoS }.Validate();
        ArgumentNullException.ThrowIfNull(request.Payload);
        var payload = request.Encoding switch
        {
            MqttPayloadEncoding.Utf8 => Encoding.UTF8.GetBytes(request.Payload),
            MqttPayloadEncoding.Base64 => Convert.FromBase64String(request.Payload),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Encoding))
        };
        var result = await _connection.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(request.Topic).WithPayload(payload).WithQualityOfServiceLevel((MqttQualityOfServiceLevel)request.QoS)
            .WithRetainFlag(request.Retain).Build(), Stopping).ConfigureAwait(false);
        if ((int)result.ReasonCode >= 128) throw new InvalidOperationException($"MQTT publish refused: {result.ReasonCode}.");
        return [message.WithValue(MqttJson.Value(new { request.Topic, ReasonCode = result.ReasonCode.ToString() }))];
    }
}
