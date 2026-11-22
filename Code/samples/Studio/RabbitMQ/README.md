# RabbitMQ explicit acknowledgement

Open `explicit-acknowledgement.fluxflow` in Studio with its local companion and Docker available.
The local environment starts a disposable RabbitMQ AMQP 0.9.1 broker. Run publishes
the bytes `00 FF 01` every 100 milliseconds; the consumer passes each delivery to
a Logger before a separate Acknowledge component settles it. Inspect Logs, Output
and Trace, then Stop to remove the owned container. Retained observations remain visible.

The publisher and consumer explicitly declare the durable `trial` queue. For an
existing broker, configure its connection and choose an existing service in the
environment. Remove the declaration lists if the account has no provisioning
permission. Stop never deletes queues or exchanges on an existing broker.

The `Body` value `AP8B` is Base64, not decoded text. Delivery includes an opaque
settlement token. The Acknowledge component references `consumer` in the same
workflow; this avoids a feedback cycle, which the engine does not permit.
Acknowledgement happens after Logger forwards the record, not when the consumer
buffers it. This does not provide a transaction across downstream systems.

The sample uses the disposable broker's guest account. For authenticated managed
brokers, supply paired username/password environment-variable references in its
service setup; credentials are resolved by the companion and are not stored in
the project. Existing-service connections use their own credential references.
Publisher confirms establish broker acceptance, not downstream completion.
Ambiguous publications are never retried automatically.
