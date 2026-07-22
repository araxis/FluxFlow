# FluxFlow.Components.Sessions.Composition

Optional `FluxFlow.Composition` registrations for canonical Sessions nodes.
Hosts provide a keyed `ISessionStore` or `ISessionStoreFactory` and may provide
a keyed `TimeProvider`; this package owns none of those resources.

Existing definitions using `session.recorder` remain supported as a hidden
alias; new definitions and Designer palettes use `session.record`.

## Registration

```csharp
services.AddKeyedSingleton<ISessionStoreFactory>("sessions", sessionStoreFactory);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterSessionRecorder()
        .RegisterSessionReplay()
        .RegisterSessionQuery());
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

The `store` resource is required. Direct keyed stores remain host-owned;
factory leases are opened during composition build and disposed with composed
nodes. The optional `clock` resource controls deterministic timestamps and
replay pacing. The similarly named `store` option remains diagnostic metadata
and does not select a DI resource.

## Typed Compatibility

Register released typed contracts under distinct caller-selected node types:

```csharp
registry
    .RegisterSessionRecordOutput("session.record.typed")
    .RegisterSessionReplayRecords("session.replay.typed")
    .RegisterSessionQueryResultBranches("session.query.typed");
```

Compatibility nodes retain Errors and Events; typed query also retains the
`Sessions` branch. Requiring explicit type names prevents a compatibility
registration from silently replacing canonical defaults.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`SessionsComponentDesignMetadataProvider` describes canonical fixed ports,
option section/importance/editor hints, the omitted typed-only
`emitSessionOutputs` control, and host-owned picker hints for `store` and
`clock`. Metadata does not create stores, open leases, execute nodes, or own
runtime state.
