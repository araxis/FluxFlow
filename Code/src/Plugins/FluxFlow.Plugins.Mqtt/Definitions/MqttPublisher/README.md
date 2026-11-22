# MQTT Publisher

Sends messages to an MQTT topic through a named `mqtt.client` connection.
Configuration type: `mqtt.publisher`.

## Setup

Set **Connection** to the resource name, such as `broker`. Add one MQTT Connection
Controller for that resource to run its connection lifecycle. Multiple publishers
can share it. The publisher does not start the broker or wait for the client to connect.

## Ports and message value

Send this value to **Input**:

```json
{
  "Topic": "factory/temperature",
  "Payload": "21.5",
  "QoS": 1,
  "Retain": false
}
```

**Output** reports the topic and publish result, retaining message correlation.
**Error** shows processing errors from Output, including offline publishing and broker
rejection. **Events** is the component event port.

For QoS 1, success requires a broker acknowledgement (PUBACK). If the acknowledgement
is lost, the connection timeout produces a correlated error even if the broker received
the message. Reconnecting does not replay the publish. Retrying it in the workflow
can deliver the payload twice.

Payload is a string. For structured JSON, serialize it first, for example with
JSONata `$string(...)`. For binary data, use a Base64 payload and `"Encoding":"Base64"`.
A successful publish acknowledges the MQTT operation, not downstream business processing.

`OutputCapacity` defaults to 128. Broker address, authentication and reconnection belong
to the connection definition in the Connections menu.
