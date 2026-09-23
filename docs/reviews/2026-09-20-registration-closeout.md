# Registration cleanup and Gate/Source configuration — 2026-09-20

Completed the agreed bounded cleanup, then added two configuration integrations using
the existing Scheduler registration approach.

## Cleanup

Workflow's event-registration guard now stores only component names in a HashSet;
the dictionary's native source values were unused. Named event outputs are initialized
with their event flag directly, removing repeated port validation and tuple mutation.
Both overloads remain: observation-only registration and a named routable output have
different behavior. The existing bridge still owns stamped event forwarding and cleanup.
No new event abstraction or class was needed. Active API/registration/package documents
were updated; historical dated reports were not rewritten.

## Configuration

GateConfiguration and SourceConfiguration are data-only DTOs in FluxFlow.Configuration.
Independent AddGate/AddSource methods register keyed ComponentRegistration delegates.
They bind/validate settings, register existing factories and native port selectors,
and leave instance creation to BuildApplicationAsync. Component implementations remain
unchanged and do not reference Configuration.

Gate supports InitiallyOpen (false by default) and positive OutputCapacity (128 default).
Input/Open/Close/Output are registered; Open/Close are native signal controls. Other
execution settings retain Gate's defaults in this initial configuration integration.

Source accepts ValuesJson, a string containing a JSON array, plus positive OutputCapacity.
The user delegated the simplest correct representation after discussing strong typing.
A heterogeneous list has no single target CLR type; IConfiguration cannot distinguish
an original number from a string with the same text. ValuesJson therefore preserves
the exact payload types through standard System.Text.Json parsing at registration.
JsonFlowValue owns each snapshot; each Source created at Build has an independent cursor.
No file loader or general serializer was introduced. Escaping embedded quotes is the
documented usability tradeoff. Empty arrays are valid. Source still emits one next value
per ordinary input and stops producing at exhaustion; scheduling remains Scheduler's job.

Configuration now references Core, Scheduler, Routing and Source. This expands the
configuration package dependencies, not the dependencies of Core or the components.

## Validation

- Release solution build: zero warnings/errors.
- Full regression: 2,581 passed, zero failed/skipped, 29 TRX reports.
- 28 new executed cases across net8/net10 cover Gate controls/error forwarding/drop,
  native port identity, captured settings, Source JSON type fidelity/lineage/exhaustion,
  independent builds and invalid-setting rollback.
- Existing event routing, observation, lifecycle and cleanup regressions passed.
- Public API source baseline reviewed/updated for the new configuration DTOs/extensions.
- Fresh candidate `0.0.0-local.20260919220554`: package-only consumer passed net8.0/net10.0,
  resolving exactly five FluxFlow packages and executing Scheduler → Source → Gate →
  async sink with owned-resource cleanup. No manual application ingress API was used.
- Working-tree whitespace check passed.

Evidence: `artifacts/registration-closeout-20260920/`. No commit, push or publication.

This completes the cleanup and these two configuration slices. Further component setup
should follow actual requirements; Mapper's expression-engine and string/file semantics
still need a deliberate design before implementation.
