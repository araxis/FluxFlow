# Public output endpoint cleanup — 2026-09-19

The agreed configuration-driven path prepares factories and links, builds the
application, then starts its sources. Consumers no longer need to retrieve a
FlowOutputEndpoint from Application or Workflow to compose that path.

## Completed

- Deleted application/workflow GetOutput. Existing GetComponent remains.
- FlowLink exposes native Input/Output Dataflow blocks and their InputAddress/
  OutputAddress strings. Its output no longer exposes an application-bound wrapper.
- Made FlowOutputEndpoint internal. Existing LinkFactory helpers remain the public
  way to prepare routes and policies; their syntax is unchanged.
- Application completion enumeration uses each output's existing link collection;
  it no longer obtains the internal output owner through the public FlowLink.Output.
- Migrated package consumers to register capture sinks and LinkFactory delegates
  before Build. They no longer fabricate a provider containing public endpoints.
- Legacy scenario tests use test-only output inspection helpers. They are not
  shipped APIs. Existing native identity assertions and public API guards were updated.

This is a breaking development-preview API change. No compatibility facade or new
production class was introduced. Internal routing state was **not deleted**: the
endpoint still owns declarations, fallback/discard policy and validation. The router
still coordinates bounded fan-out and completion. Making a class internal simplifies
the consumer contract; it does not itself simplify all of that implementation.

## Verification

- Release solution build: zero warnings/errors.
- Full solution regression: 2,545 passed, zero failed/skipped, 29 TRX reports.
- Reviewed public declaration baseline updated; ordinary baseline check passed in
  the full run. Source scanner also includes public declarations within internal types.
- Fresh package-only consumers passed on net8.0 and net10.0:
  - Core: `0.0.0-local.20260919213307`.
  - Routing: `0.0.0-local.20260919213329`.
  - Configuration/Scheduler: `0.0.0-local.20260919213346`.
- Core consumer verifies FlowLink.Output is the same native source registered in DI,
  plus document/address/setup links, error recovery, payload identity and revision rollback.
- Scheduler consumer verifies configuration-driven generation and delivery.
- Working-tree whitespace check passed.

Evidence is in `artifacts/output-api-cleanup/`. Packages were built after source
regression, sequentially. No publication, commit, push or external adoption occurred.

## Next boundary

Review physical removal of the internal output wrapper while retaining output-local
route state, validation, delivery policies, fallback and completion dependencies.
Do not substitute a renamed manager or application-wide parallel link table.
Revision-host ApplicationPorts remains a separate existing API.
