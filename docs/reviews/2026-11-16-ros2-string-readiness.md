# ROS 2 standard String readiness

## Change

The ROS plugin now registers `ros.publisher` and `ros.subscriber` for
`std_msgs/msg/String`, using the existing generic registrations and lifetime APIs.
The generated binding ships inside the plugin. Existing custom typed registrations
are retained. Configuration keys and port names are unchanged.

The portable design continues to use the shared Property Grid for Node, Topic, QoS,
queue settings and output capacity. Node selection offers application resources or
resources in the current workflow; unavailable saved references remain intact.
The plugin-owned environment adapter recognizes ROS nodes as local resources with
no external endpoint. Configuration checks do not instantiate native ROS.

The message contract is `{"Data":"hello"}`. Successful Publisher Output keeps the
original envelope and confirms local publication. Subscriber creates a new envelope
with a snapshot of the received message. This wire type does not transmit FluxFlow
correlation or trace metadata. Conversion and publication errors retain the existing
component error behavior.

## Verification

- ROS controlled tests: 27 per target on .NET 8, 10 and 11. Standard String JSON,
  Unicode, strict unknown-field handling, custom registrations, subscription failure,
  deferred native activation, disposal ordering, Stop and workflow restart passed.
- The .NET 11 generic host aggregates its original background-service failure during
  Stop. Tests assert that every retained inner exception is the original exception;
  no exception is rewritten or suppressed.
- Companion, ROS preparation and execution log/trace checks: 59 per target passed.
  The palette compatibility expectation now includes both ROS components as registered.
- Studio suite exercised 1,997 tests per target. All behavior checks passed; after
  updating an obsolete native-prerequisite text assertion, all 38 palette tests per
  target passed. ROS-specific coverage includes QoS visual edits, unsupported saved
  queue modes, missing references, exact saved fields, save/reopen, missing-plugin
  round trips and Undo/Redo.
- The packaged plugin was installed in an isolated store. Self-contained Linux hosts
  on .NET 8, 10 and 11 loaded the package through InstalledPlugins without a ROS plugin
  project reference. Independent rclpy endpoints on ROS Jazzy exchanged exact String
  values containing Unicode, newlines and quotes. Each host completed two full runs,
  observed native endpoint disappearance after Stop and rejected cross-run leakage
  from messages sent while stopped. The final package also passed on .NET 11 after
  removal and reinstall; the empty store was checked between those operations.
- Plugin builds and packaging passed for net8.0/net10.0; the .NET 11 host uses the
  compatible net10.0 plugin payload. Existing unrelated analyzer warnings remain.
- All 63 protected Core/WFENG files matched the existing SHA-256 baseline.

Reproduction host and peer: `Code/eng/ros2-plugin-consumer`.
Evidence package and isolated store: `%TEMP%/fluxflow-ros-readiness`.

## Native limits

Native delivery was verified on Ubuntu 24.04 in WSL with ROS Jazzy, using localhost
discovery, domain 173 and unique test topics. Windows native ROS delivery, other ROS
distributions, remote networks and other middleware implementations were not tested.
The package does not install ROS or its std_msgs type-support libraries.

Rcl.NET PublishAsync has no cancellation token. Stop waits for an in-flight publish;
a blocked native call can prevent timely cleanup. Native QoS/discovery compatibility
still applies, and subscription drop policies can lose messages. No delivery ACK,
durability or remote-processing guarantee is added. This completion covers one
standard String message type; robot-specific messages, services and actions are
outside its scope.

No saved user projects, plugin stores or browser tabs were changed. Core and WFENG
were not modified.

## Follow-up review

Reproduced and fixed an explicit-null String payload gap: `{"Data":null}` previously
deserialized successfully and reached PublishAsync. The generated CLR property is
non-nullable, but that annotation alone does not reject JSON null. A small partial
class implements the existing System.Text.Json deserialization callback and rejects
null before native publication. The generated ABI file and generic ROS converters
remain unchanged. Empty strings and the existing `{}` empty-string default remain
valid. Tests also verify that the next valid input succeeds after an invalid payload.

The controlled restart fixture now uses a concurrent queue for disposal observations,
so asynchronous endpoint cleanup cannot race writes to its test bookkeeping.
No additional transport, registration framework or shutdown abstraction was needed.

Follow-up verification passed on .NET 8, 10 and 11:

- 93 ROS controlled test executions, including null/scalar/array rejection, strict
  fields, recovery after conversion errors, custom registrations and Stop/rerun.
- 327 focused Studio executions covering ROS Property Grid fields, missing-plugin
  save/reopen, saved wiring, resource filtering and Undo/Redo.
- 177 companion and original execution log/trace contract test executions.
- Six native runs through a fresh installed package, two on each target. Independent
  rclpy endpoints verified empty strings, Unicode, quotes/newlines and non-BMP text;
  endpoint disappearance after Stop and stale-message isolation passed.
- Self-contained Linux test hosts published without warnings. The Studio test build
  still reports existing unrelated analyzer warnings.

Follow-up package and isolated store: `%TEMP%/fluxflow-ros-followup`.
Native limits above remain unchanged. All 63 protected files still match the
baseline. The user's running Studio, plugin store, projects and tabs were untouched.
