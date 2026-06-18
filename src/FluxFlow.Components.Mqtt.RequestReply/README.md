# FluxFlow.Components.Mqtt.RequestReply

MQTT request/reply **trigger** for FluxFlow — the same `RequestReplyBridge` the HTTP
trigger uses, wired to MQTT. Proof that the bridge is transport-neutral: pub/sub here,
HTTP there, identical correlation machinery.

It references **no MQTT library**. The host owns the MQTT client (MQTTnet, …): it maps
inbound messages onto `MqttRequest`, feeds them to the bridge, and implements
`IMqttResponsePublisher` to publish replies — exactly as the HTTP node takes an injected
`HttpClient` and the ASP.NET adapter owns Kestrel.

```csharp
var bridge = new RequestReplyBridge<MqttRequest, MqttReply>();
bridge.Output.LinkTo(handler.Input);     // your graph: FlowMessage<MqttRequest> -> FlowMessage<MqttReply>
handler.Output.LinkTo(bridge.Responses);

// in the host's MQTT subscription handler (MQTTnet etc.):
mqttClient.ApplicationMessageReceivedAsync += async e =>
{
    var request = new MqttRequest
    {
        Topic = e.ApplicationMessage.Topic,
        Payload = e.ApplicationMessage.PayloadSegment.ToArray(),
        ResponseTopic = e.ApplicationMessage.ResponseTopic,
        CorrelationData = e.ApplicationMessage.CorrelationData,
        ContentType = e.ApplicationMessage.ContentType
    };
    await bridge.SubmitAsync(request, myPublisher); // myPublisher : IMqttResponsePublisher over mqttClient
};
```

`MqttRequestContext` seeds the correlation id from MQTT5 correlation data when present,
and `ReplyAsync` publishes the graph's reply to the request's **response topic**,
echoing the original correlation data so the requester can match it. A request with no
response topic is fire-and-forget (the reply is dropped). Timeouts are the requester's
concern — there is no standard MQTT error reply.
