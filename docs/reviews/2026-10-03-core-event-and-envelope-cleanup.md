# Core event and envelope cleanup

This follows the native Dataflow refactoring and preserves its publication contract.
No new public type, configuration option or dependency was introduced.

## Changes

ComponentEventBridge now uses a native TransformBlock to add address headers, linked
to its BroadcastBlock with native completion propagation. The manual SendAsync
forwarder and its output-completion/fault relay are removed. The bridge takes the
existing ComponentAddress rather than rebuilding it from workflow/component strings
and storing three overlapping string fields. Its source is required, as it already
was at the registration boundary.

Component completion still controls event detachment; component faults remain on
the component's own completion and do not fault the observation stream. A failed
event output faults the transform too, releasing pending results. Premature output
completion also stops the transform instead of leaving results without a receiver. This short reverse
fault observation is necessary because native completion propagation only flows
downstream. Event unlink failures still join cleanup and retain exception identity.
DI remains the sole disposal owner.

FlowMessageJsonConverter has one optional-value reader instead of separate class
and struct readers. Nullable identifier types express absence directly. Required
fields, unknown/duplicate rejection, value/error shape, structured headers, typed
deserialization and source-generated metadata behavior remain unchanged.

Processing and application lifetimes use AggregateException.GetBaseException in
place of recursive/manual single-inner unwrapping. Multiple-inner aggregates stay
intact. Cancellation, error-data conversion and shutdown order are unchanged.

## Validation

Regression coverage checks event identity, timestamps, lineage and immutable source
headers; a failed event output cannot strand transform completion. Existing tests
retain fanout, late observation, component-fault isolation, unlink failure and failed
attachment behavior. Envelope tests cover omitted and explicit-null optional fields
for both common and typed messages.

The final Release suite passes all 1,966 test executions across .NET 8 and .NET 10,
with no warnings. The isolated Core package consumer passes on both frameworks.
Whitespace validation is clean. The source-declaration
baseline was refreshed because it also scans the public constructor of the internal
event bridge; no externally accessible signature changed.

The remaining source/processing lifecycle distinction is intentional: started
production and message processing have different stop and control-drain behavior.
Combining them would add mode branches. The envelope wire format and event payload
format are retained rather than introducing an unrelated migration.
