# Remove the input endpoint wrapper

FlowInputEndpoint was a second object around an already registered native target.
It repeated the application reference, component owner and port address, and exposed
another linking API. This step physically removes that class without replacing it
with another class or interface.

Workflow input registrations retain only the native block, existing port validation
metadata and control identity in tuple values keyed by full address. Metadata is
still needed for signal validation, link cardinality and revision compatibility;
control identity is still needed for source/control drain ordering. Removing the
wrapper does not mean those requirements disappeared.

The Build-only DI provider registers the actual ITargetBlock<FlowMessage> by address.
A plain read-only address collection supports discovery because keyed DI enumeration
returns values rather than keys. There is no input-endpoint service registration.
Instance registration retains application ownership and does not let DI dispose ports.

FlowLink now contains InputAddress and the native Input. FlowRouter delivers directly
to that block. Application completion derives the receiving component from the address
and reads control identity from the workflow registration. Revision compilation, port
binding, compatibility checks and staged drain use the same workflow data.

## Usage and migration

```csharp
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.source.Output", "other.sink.Input"));

// Application lookup and keyed DI both return the original native target.
ITargetBlock<FlowMessage> input = app.GetInput("main.source.Input");
await app.StartAsync();
await app.SendAsync("main.source.Input", message, cancellationToken);
```

- GetInput previously returned FlowInputEndpoint; now it returns ITargetBlock.
  Remove `.Block` when accessing the native input.
- Application.SendAsync previously accepted an endpoint; now it accepts its full
  address. Its running-state checks and linked shutdown/caller cancellation remain.
  Direct calls to the native block use Dataflow admission without application guards.
- Output LinkTo/OtherwiseTo and To take a full input address. Input-first From chains
  use that address. LinkFactory's ports.Link and LinkFallback signatures are unchanged.
- FlowLink.Input is a native target; use InputAddress for its registered address.
- An address resolves within the owning application. A foreign-only address fails
  lookup; it is no longer an endpoint-object ownership ArgumentException. A mixed DI
  provider supplying another application's native target under an identical address
  is explicitly rejected before a route is added.

This is a breaking development-preview API change. Consumers and source API baseline
were migrated; it is not a compatibility shim or a published package version. The
working preview remains 1.0.0-alpha.1; qualification uses unique local package versions.

## Verification

- Release solution build: zero warnings/errors.
- Full suite: 2,543 passed, zero failed/skipped, 29 TRX reports.
- Two new cases on net8/net10 verify alias/native identity, independent builds with
  identical addresses, address-based admission, post-stop rejection and mixed-provider
  cross-application rejection. Existing metadata/control, backpressure, fan-in, fallback,
  revision replacement and disposal tests were migrated and retained.
- Package-only Core, Routing and Configuration/Scheduler consumer results are recorded
  in artifacts/input-endpoint-removal/verification.json and their package logs.
- The existing workflow dictionary is accessible internally to composition lookup;
  no second map was introduced. Per-send lookup does not allocate a public Workflows
  snapshot. Public snapshot behavior remains tested. No performance claim was measured.

FlowOutputEndpoint still owns output route declarations and policies. FlowRouter and
completion coordination also remain. This is a completed input-wrapper removal, not
full endpoint removal. Review output responsibilities next without moving them into
another large builder or deleting delivery behavior.

No commit, push or publication. Evidence: artifacts/input-endpoint-removal/.
