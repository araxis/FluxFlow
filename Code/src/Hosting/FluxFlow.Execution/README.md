# Local execution

The companion registers the bundled component configuration APIs in
`WorkflowExecution.RegisterComponents`. Validation and startup use the same registrations.
The runtime does not depend on Studio descriptions; the companion regression test
compares these registrations against the palette supplied by Authoring DI.

Scheduler, Source, Data Generator, Mapper, Batch, Reducer, Gate, Router,
Change Filter, Deduplicate, Rate Limit, Request–Reply, Retry, HTTP Client, BLE
Client/Notifications/GATT Server and Logger use bundled configuration APIs.
Installed plugins contribute their own registrations to validation and execution:

- NATS and JetStream share one plugin, including the `nats.connection` resource.
  Install it and restart the companion before running existing NATS workflows.
- MQTT, RabbitMQ and Serial each require their respective plugin. Broker clients
  need the configured endpoint; Serial needs a workflow-local `serial.port`
  resource and an accessible serial device on the execution machine.
- Rate Limit requires a host-provided `RateLimiter` resource; the generic companion
  does not define a limiter resource factory.
- Request–Reply creates its bounded concurrency limiter through its existing
  configuration API and reuses the Rate Limit runtime.
- BLE needs an `IBlePlatform` implementation and a capable adapter on the execution
  host. The Windows Studio host registers its Windows backend; the generic runner
  alone cannot access Bluetooth. Capability checks do not prove peripheral access.

The palette also supports designing components for specialized hosts. These are
not runnable in the generic companion yet:

- HTTP Trigger requires `AddHttpTriggers()` and `MapFluxFlowHttpTriggers()` in the
  workflow's ASP.NET Core host. The companion's management endpoints do not supply
  workflow routes.
- ROS 2 Publisher and Subscriber require `AddRosNode()` and
  `AddRosPublisher<T>()` / `AddRosSubscriber<T>()` for a generated ROS message type,
  plus the matching native ROS installation. A message type is not inferred.

Unsupported registrations fail before environment preparation. Project settings
and workflow configuration are not rewritten to substitute other components.

Check environment first checks the companion's runtime limitations and validates
configuration through the same registration APIs used by startup. It reports
component addresses for unsupported hosts, missing limiter support and invalid
serial configuration. It never resolves component/resource factories or opens
serial devices. Docker is probed only after these checks and environment validation
succeed, and only when container setup is requested. A successful check does not
prove device access, expression compilation or endpoint connectivity.

## Lifecycle ownership

- `LocalEnvironmentRunner` owns run IDs, status and retained observations.
- `WorkflowExecution` prepares the environment and starts application instances.
- `WorkflowRun` stops instances before cleaning up the prepared environment. It
  attempts all cleanup steps even when an earlier step fails.
- `WorkflowInstanceRun` owns one host, application and output capture. The
  application is registered as an externally owned instance so its execution fault
  cannot interrupt DI disposal of other resources.

Instance cleanup distinguishes an already-recorded execution fault from a new
cleanup failure by exception identity. It does not rewrite or filter log records.
The cleanup task is retained, including failure, because a second native disposal
may be a no-op and cannot prove that a failed first disposal succeeded. Environment
cleanup remains independently retryable. The ten-second host Stop deadline is an
internal lifecycle bound, not a project setting or a deadline for all disposal.
