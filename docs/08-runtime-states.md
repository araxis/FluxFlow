# Runtime States

`FluxFlowApplication.State` exposes one canonical lifecycle model:

| State | Meaning |
|-------|---------|
| `Empty` | Registered but no start or apply has activated an application. |
| `Starting` | Loading or preparing the first revision. |
| `Running` | A revision is active and the last mutation succeeded. |
| `Reloading` | Preparing a replacement while the prior revision remains active. |
| `Degraded` | The latest mutation was rejected while a prior revision may still run. |
| `Stopping` | Draining and disposing active runtime ownership. |
| `Stopped` | Shutdown completed; the instance cannot be restarted. |

Use `Current` for the active `ApplicationSnapshot`, `CurrentDefinition` for its
canonical definition, and `LastUpdate` for the most recent result. There is no
separate load-result or revision-host state model.

```csharp
var application = provider.GetRequiredService<FluxFlowApplication>();
var update = await application.ReloadAsync("deployment-43");

if (update.IsRejected)
{
    foreach (var diagnostic in update.Diagnostics)
        Console.Error.WriteLine($"{diagnostic.Stage}: {diagnostic.Error.Code}");
}
```

Expected source, definition, resource, component, or activation failures return
`Rejected`. When a previous revision exists, it remains active and the
application becomes `Degraded`; a later successful update restores `Running`.
Cancellation does not become a rejected result.

Use canonical stable addresses through `application.Ports`:

```csharp
var sent = await application.Ports.SendAsync(
    "Orders.Receive.Input",
    FlowMessage.Create(order));
```

Port operations report their own accepted/full/unavailable/completed outcomes.
Component errors remain `FlowError` data on normal outputs and do not change the
whole application state. Unrecoverable structural runtime faults remain
observable through completion and diagnostics.

For an operational UI, show application state, current revision ID, latest
update diagnostics, stable-port status, system events, and component Events as
separate signals. Do not infer application failure from an ordinary message
error.
