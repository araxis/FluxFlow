# FluxFlow.Components.Sessions

Standalone session recording, replay, and query nodes over a host-owned session
store.

- `SessionRecorderNode`: `SessionContentRecordInput` -> `SessionContentRecord`.
- `SessionReplayNode`: source of `SessionContentRecord` with pacing and
  cancellation.
- `SessionQueryNode`: `SessionQueryRequest` -> `SessionQueryOutcome`.

Record content uses exact `FlowContent`; adapter-facing store records remain
neutral and deterministic versioned JSON preserves content bytes and metadata.
Reads continue to accept records written with the earlier private envelope.
Query bounds, sequence continuation, replay pacing, completion, deterministic
clocks, and fan-out remain intact.

Expected query outcomes are typed. Store or operation failure becomes
`FlowError` on normal Output. The host owns the store and optional clock.

## Keyed Store Registration

Session stores use standard keyed DI. Use the exact canonical resource address
from the application definition as the key:

```csharp
services.AddKeyedSingleton<ISessionStore>(
    "Resources.Sessions.Primary",
    (provider, _) => CreateSharedSessionStore(provider));

services.AddKeyedSingleton<ISessionStoreFactory>(
    "Resources.Sessions.PerSession",
    (provider, _) => new ApplicationSessionStoreFactory(
        provider.GetRequiredService<ApplicationSessionDatabase>()));
```

Composition resolves a keyed `ISessionStore` before an
`ISessionStoreFactory`. A direct store is shared and remains host-owned. A
factory receives `SessionStoreContext` with the exact store key, configured
session ID, and resolved clock; its `SessionStoreLease` declares whether the
opened store is shared or owned. Disposing an owned lease disposes the store,
preferring asynchronous disposal when available. Disposing a shared lease does
not dispose the store. DI separately owns and disposes singleton instances it
constructs; an instance supplied directly to `AddKeyedSingleton` remains the
host's disposal responsibility.

## Composition

Install `FluxFlow.Components.Sessions.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
