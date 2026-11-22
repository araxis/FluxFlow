# RabbitMQ Publisher

Publishes AMQP 0.9.1 messages using a named `amqp.connection`. This is independent
of RabbitMQ's MQTT plugin.

Component type: `amqp.publisher`.

## Settings and input

Use the Property Grid to select a RabbitMQ connection and set the publish timeout,
buffer capacity and maximum message size. **Broker declarations** contains
expandable **Exchanges**, **Queues** and **Bindings** groups. Add and remove items
in each group and edit their properties directly in the grid. Each committed edit
supports Undo/Redo. Empty lists use existing broker entities without declaring them.
Each declaration also has an **Arguments** collection with Add/Edit/Remove.
Its visual editor supports explicit AMQP types, including nested arrays and tables.
Bytes use Base64; Int64 values retain exact decimal text. Save validates against the
existing configuration API and commits one undoable edit. Cancel discards the draft.
**Advanced JSON** remains optional for the complete component configuration.

Each input must be a publish request. For example, connect a Mapper's Output to
Publisher.Input and have the Mapper produce the JSON shown below. `Exchange`,
`RoutingKey`, `Body`, `Properties` and `Mandatory` belong to that message; they are
not fixed Publisher settings. A plain payload is not a complete publish request.

## Ports

- **Input:** a publication with `Exchange`, `RoutingKey`, Base64 `Body`, optional
  `Properties` and `Mandatory` (true by default).
- **Output:** `Status: "Confirmed"` and the original `MessageId` after a broker
  confirmation. Failure uses the existing workflow error contract.
- **Events:** standard component lifecycle events.

```json
{
  "Exchange": "",
  "RoutingKey": "readings",
  "Body": "AQID",
  "Mandatory": true,
  "Properties": {
    "ContentType": "application/octet-stream",
    "MessageId": "reading-1",
    "CorrelationId": "request-1",
    "Headers": { "device": { "Type": "String", "Value": "sensor-1" } }
  }
}
```

`Body` preserves arbitrary binary bytes. Header values have explicit AMQP types;
binary fields use `Bytes` with Base64, and 64-bit integers use `Int64` with decimal
text to avoid precision loss. An empty exchange uses RabbitMQ's default exchange.

## Connection and declarations

Create a RabbitMQ connection in Connections. Configure `Endpoint`, `VirtualHost`,
recovery and timeout there. Production credentials use
`UsernameEnvironmentVariable` and `PasswordEnvironmentVariable`: names of variables
available to the companion's account. Do not put their values into the project.
With neither reference configured, the native client uses RabbitMQ's guest account.
For TLS, use `amqps`; certificate and hostname validation remain enabled.

`Topology` optionally declares exchanges, queues and bindings. Empty lists make
no declaration calls. Declarations are not destructive reconciliation: incompatible
existing entities cause a broker error, and Stop never deletes external entities.

## Delivery and failures

Publisher confirms acknowledge broker acceptance, not downstream processing.
Mandatory unroutable messages fail. Timeout, cancellation or a lost connection can
leave the outcome unknown. The component does not retry ambiguous publications;
an application that retries must account for duplicates.

Capacity and maximum message size are bounded. Stop closes the owned channel;
the connection is disposed with its run. A managed environment requires Docker;
an existing broker does not.
