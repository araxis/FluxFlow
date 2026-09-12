# Minimal package execution boundaries

The current local candidate, `0.0.0-local.20260911.t1400`, runs ordinary components
and graphs without an application host, container, registration catalog or editor.
This is verified by fresh package-only consumers, not inferred only from project
references or an all-packages consumer.

## Restored dependency sets

| Consumer | net8.0 | net10.0 |
| --- | --- | --- |
| Core-only | Core and Dataflow 9.0.4 | Core only; Dataflow is runtime-provided |
| Direct Fluent | Fluent, Core and Dataflow 9.0.4 | Fluent and Core; Dataflow is runtime-provided |

These are complete restored package-library sets, excluding implicit runtime
framework references. No project-reference libraries, host, composition, document,
designer, evaluator or durable-provider packages occur. Every internal coordinate
uses the candidate version. Four isolated caches and archive/output hashes prove
the actual local candidate was consumed rather than repository project outputs.

## Executed behavior

The artifact-local Core fixture uses directly constructed small FlowNode subclasses:

- `Standalone_component_processes_without_application`: actual public input/output
  blocks, exact transformed results and derived identities, then completion.
- `Instance_graph_preserves_fanout_identity`: `Add(name, instance)`, two workflows,
  LinkTo/LinkFrom, a native predicate including the zero boundary, original message
  references through fanout, drain and owned disposal.
- `Shared_trace_requests_receive_distinct_replies`: eight requests held concurrently
  before release, unique exchange correlations, exact replies/lineage and no
  remaining pending entries.

All three pass on both target frameworks. The existing Fluent sample is copied
unchanged; its full linear and branching output is checked exactly on both targets.
This also exercises directly constructed sources and shared-sink fan-in through
the minimal Fluent package closure.

Artifacts: `artifacts/core-refactoring/minimal-consumers-20260911`. `Program.cs`
contains the runnable Core examples. `run.ps1 -Attempt runtime-aware` names the
successful evidence directory; use a new attempt name to rerun with fresh caches.
`verify.ps1` validates the final checkpoint. All twelve consumer commands pass;
the four consumer builds and the full nonincremental solution build have zero
warnings/errors.

## Provenance and limitations

No production, permanent test or existing sample sources changed. The 1,485-input
qualification snapshot remains unchanged. After rebuilding, all 162 package target
assemblies and symbol entries still match the candidate. The prior 4,828 local
regression passes/5,038 discovery remain linked evidence, not a suite rerun here.
These console fixtures are explicitly invoked acceptance checks, not new discovered
unit cases. Assertion and static pseudo-mutation review: `.testagent/minimal-consumers-review.md`.

The first attempt incorrectly expected a net10 Dataflow package dependency and is
retained under `qualified`. Restored assets identify it as runtime-provided; the
corrected framework-specific expectation passed in fresh `runtime-aware` consumers.
No production dependency was added or removed to make this check pass.

This closes minimal Core/direct-Fluent package execution qualification. It does not
prove all modules, optional authoring/host convergence, error/concurrency boundaries,
telemetry integration, sustained performance or complete feature migration. The
next architecture work should audit actual execution owners against the capability
ledger and correct historical migration wording, rather than using dependency
closure alone to claim one runtime. Nothing was published or externally coordinated.
