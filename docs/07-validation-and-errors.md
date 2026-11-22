# Validation And Failure Semantics

Ordinary composition validates the configuration and registered links before startup.
Configuration registrars reject malformed sections and link syntax with their location.
Build resolves ports and compiles conditions. FlowApplication composes native factories and links through
BuildApplicationAsync. There is no revision planning or update activation stage.

If Build fails, partial processing is stopped/drained and the host provider remains
responsible for disposal. Cancellation remains cancellation. Fix the configuration and
construct a new host/provider; failed Build does not restore a previous live application.

## Input Validation

Components own deserialization and domain validation. Read FlowValue.Json using
local System.Text.Json metadata, or reuse a compatible typed FlowValue directly.
No framework materializer interface or result wrapper is required. Runtime
processing failures retain their FlowError and original diagnostic exception.


## Runtime Channels

Canonical components follow one model:

| Channel | Contract | Use |
|---------|----------|-----|
| `Output` | `FlowMessage` | A typed `FlowValue` or `FlowError` that workflow logic may handle. |
| `Events` | `FlowMessage` with a non-null canonical `FlowValue` | Lifecycle, diagnostics, observations, warnings, and metrics. |
| `Completion` | `Task` | Unrecoverable implementation, infrastructure, or lifecycle failure. |

There is no new universal `Errors` port. A validation rejection, HTTP failure,
storage miss, or protocol command failure that the workflow can inspect is a
normal result value. Links may condition on it:

```json
{
  "Output": {
    "Address": "errorHandler.Input",
    "Condition": "message.IsError",
    "Engine": "csharp"
  }
}
```

This is an output property inside a component section; `errorHandler.Input` must
be a registered input. The C# engine exposes the original message as `message`.
Use a mapper when a downstream component requires a different result shape.
Do not fault component completion for expected per-message outcomes.

Bundled configuration adapters also accept `"Error": "errorHandler.Input"` as a
shortcut for this error filter without requiring an engine. It selects IsError
messages from Output and creates no physical Error port. Output still carries errors;
if both properties are configured, an error can go down both paths. Error also accepts
conditional objects and arrays with the usual message context.

Link conditions returning false are normal filters. If no link matches, the message
is consumed without downstream delivery or a routing fault; registered outputs with
no links also drain. Fallback is removed. A condition that throws or a destination
that refuses admission faults execution. Links wait for temporary capacity shortages.

## Addressable Events

Component setup can register `Events` at
`Workflow.Component.Events`. Component events are bounded, fault-isolated,
correlated where source information exists, and carried in the normal traced
message envelope. They can feed logging, metrics, mapping, conditional links,
another workflow, or direct observation.

Use the component's registered event address in ordinary links. There is no
application-wide activity port or revision event stream. Automatic error collection
is separate work; connect the event and error routes required by the application.

## Completion

Completion faults represent a condition the component cannot safely continue
through, such as a broken implementation invariant, unrecoverable external
infrastructure required for lifecycle, or a failed start/stop sequence. The
runtime observes those faults, coordinates shared-input completion, and still
attempts complete cleanup. Cleanup failures are aggregated without duplicating
the already observable runtime completion fault.

## Compatibility Boundary

Configuration registers native component and link factories; `FlowApplication`
resolves and runs them. There is no separate revision host or aggregate `Errors`
property. Observe registered component event sources and route business failures
from Output values. Observation feeds do not replace business-error handling.

Next: [Runtime States](08-runtime-states.md)
