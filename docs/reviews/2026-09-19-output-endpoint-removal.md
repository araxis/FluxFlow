# Output endpoint removal — 2026-09-19

FlowOutputEndpoint is physically deleted, completing the endpoint removal stage.
No replacement production class was introduced. FlowEndpoints.cs is now FlowLink.cs,
containing the existing FlowLink and delivery policy.

Workflow stores each output's native block, metadata, event identity, route list and
discard flag directly, keyed by full address. This parallels its existing input data.
The redundant endpoint application/owner/address object is gone; the workflow and
dictionary key already supply that context.

During Build, standard keyed DI registers native blocks plus ordinary Action delegates
for adding a route and selecting discard policy. Public LinkFactory syntax is unchanged:

```csharp
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.source.Output", "other.sink.Input"));
```

The delegates retain mutation guards, source/target registration identity and duplicate
fallback checks. Link helpers retain delivery-policy validation. Document discovery uses
the same read-only address-list approach for inputs and outputs. Revision metadata and
drain read the workflow's actual registrations. FlowRouter remains independent of those
registration details and operates on native blocks and prepared links.

Required state and validation were relocated, not eliminated. The concrete simplification
is one fewer framework type, no second output object with an application reference, and
one workflow registration as the source of output data. Tuple updates require writing the
changed value back to the dictionary; event/discard updates do so under existing guards.
Native routing, bounded fan-out, fallback, failure handling and completion coordination
remain necessary. No performance improvement is claimed.

Legacy scenario fixtures now inspect output tuples and assert native block identity.
They are test-only and not a replacement consumer API. Existing mixed-application tests
exercise the same identity guard via the registered route delegate.

## Verification

- Final Release solution build: zero warnings/errors.
- Final full regression: 2,545 passed, zero failed/skipped; 29 TRX reports.
- Initial full run: four failures from two tests still comparing wrapper reference
  identity on both target frameworks. Updated those assertions to native block identity;
  final complete run passed. Initial evidence is retained.
- Source declaration baseline reviewed/updated; public LinkFactory usage unchanged.
- Fresh package-only consumers passed on net8.0/net10.0:
  - Core `0.0.0-local.20260919214600`.
  - Routing `0.0.0-local.20260919214729`.
  - Configuration/Scheduler `0.0.0-local.20260919214743`.
- Core package preceded the test-only assertion fixes; production code was unchanged.
- Working-tree whitespace check passed. No publication, commit or push.

Evidence: `artifacts/output-endpoint-removal/`; final regression is `verified-tests/`.
Earlier `final-tests/` contains the initial failed attempt and is not the final result.

Next, review remaining composition/registration duplication before component setup.
Do not restore wrappers or collapse distinct responsibilities merely to reduce class count.
