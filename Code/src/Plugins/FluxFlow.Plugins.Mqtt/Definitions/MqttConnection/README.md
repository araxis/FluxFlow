# MQTT Connection Controller

Starts and stops an MQTT client and reports connection-state changes. Its configuration
type remains `mqtt.connection`. This is an active lifecycle component, not just a status display.

## Three different things

- **Connection definition** (`mqtt.client`): a named resource such as `broker`, created
  through the Connections menu. It holds the broker address, credentials and reconnect policy.
- **Connection Controller** (`mqtt.connection`): the canvas component that runs that
  client. Set its **Connection** property to `broker`.
- **Broker service**: the MQTT server, such as Mosquitto. Project setup chooses an
  existing service or a temporary Docker container. The controller does not create containers.

## When to use it

Keep one controller per MQTT client resource, even with `AutoConnect` enabled.
Defining the resource alone does not start network activity. Publishers and subscribers
reference the same resource; they do not replace the controller.

On workflow start, the controller runs the resource's auto-connect/reconnect loop.
With `AutoConnect` disabled, it waits for an explicit Connect command.
Stopping it disconnects the shared client and stops reconnection. This affects every
component using that client. Use a workflow-local resource for independent lifetimes,
or keep the shared controller in a workflow that stays running.
The controller borrows the client resource; DI owns its disposal.

## Ports

- **Input** accepts a connection command in the message value.
- **Output** returns the command result, for example `{"Command":"Connect","Connected":true}`.
- **Events** emits `mqtt.connecting`, `mqtt.connected`, `mqtt.disconnecting` and
  `mqtt.disconnected`. These are notifications, not a continuously polled status value.
  Exhausted retries emit the diagnostic event `mqtt.reconnect_exhausted`.
- **Error** is Studio's filtered view of errors on Output, including connection failures.

Connect explicitly:

```json
{
  "Command": "Connect"
}
```

Disconnect and stay disconnected:

```json
{
  "Command": "Disconnect",
  "DisableReconnect": true
}
```

Without `DisableReconnect`, Disconnect follows the resource's `AutoConnect` policy
and may reconnect. A subsequent Connect resumes connection attempts.

## Setup example

```json
{
  "Type": "mqtt.connection",
  "Connection": "broker"
}
```

Use **Connections → broker** to edit the address and see its consumers.
`OutputCapacity` limits buffered output messages; its default is 128.
Use the connected event or a successful Connect result to initiate publishing;
an offline publisher reports an error rather than waiting for readiness.
