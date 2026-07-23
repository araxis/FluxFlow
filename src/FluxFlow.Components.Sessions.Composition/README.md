# FluxFlow.Components.Sessions.Composition

Optional `FluxFlow.Composition` registrations for canonical Sessions nodes.
Hosts provide a keyed `ISessionStore` or `ISessionStoreFactory` and may provide
a keyed `TimeProvider`; this package owns none of those resources.

Existing definitions using `session.recorder` remain supported as a hidden
alias; new definitions and Designer palettes use `session.record`.

## Registration

```csharp
services.AddKeyedSingleton<ISessionStoreFactory>(
    "Resources.Sessions.Primary",
    sessionStoreFactory);

var registry = new CompositionNodeRegistry()
    .RegisterSessionRecorder()
    .RegisterSessionReplay()
    .RegisterSessionQuery();
```

| Type | Canonical ports |
|------|-----------------|
| `session.record` | `SessionContentRecordInput` Input, `FlowResult<SessionContentRecord>` Output |
| `session.replay` | `FlowResult<SessionContentRecord>` Output |
| `session.query` | `SessionQueryRequest` Input, `FlowResult<SessionQueryOutcome>` Output |

All canonical descriptors expose Events and no universal Errors surface.
Recorder/replay/query failures are ordinary result values, so links can branch
on `Kind`, `IsError`, `Error.Code`, or value fields.

## Flat Document

```json
{
  "Resources": {
    "Sessions": {
      "Primary": {
        "Type": "host.session-store"
      }
    }
  },
  "Workflows": {
    "OrderProcessing": {
      "BuildContent": {
        "Type": "serialize.json",
        "Output": "BuildRecord.Input"
      },
      "BuildRecord": {
        "Type": "session.record-request",
        "name": "received-order",
        "Output": "Record.Input"
      },
      "Record": {
        "Type": "session.record",
        "sessionId": "run-42",
        "store": "Resources.Sessions.Primary",
        "sessionName": "order intake",
        "Output": ["HandleResult.Input", "Audit.Input"]
      },
      "HandleResult": {
        "Type": "session.result"
      },
      "Audit": {
        "Type": "audit.result"
      }
    }
  }
}
```

`BuildContent`, `session.record-request`, `session.result`, and `audit.result`
are host example types. Composition does not insert mapping, serialization, or
request construction. A recorder command must be built explicitly from
upstream FlowContent. Resource addresses use the host application address
framework.

The `store` resource property is required and is the only store selector.
Direct keyed stores remain host-owned;
factory leases are opened during composition build and disposed with composed
nodes. The optional `clock` resource controls deterministic timestamps and
replay pacing. Both resource values use exact canonical addresses such as
`Resources.Sessions.Primary`; metadata does not infer prefixed DI keys.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits low-level `boundedCapacity` plus
inherited identity/concurrency fields from normal editing. Default execution
requires no processing profile; domain options such as `sessionName`, replay
pacing, and query filters remain available through provider metadata.


`SessionsComponentDesignMetadataProvider` describes canonical fixed ports,
option section/importance/editor hints, and host-owned picker hints for `store`
and `clock`. Metadata does not create stores, open leases, execute nodes, or
own runtime state.
