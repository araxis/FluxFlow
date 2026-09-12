# Qualified local candidate

Historical checkpoint: superseded by the
[integrated candidate](2026-09-12-integrated-local-candidate.md), which includes
the later source-event shutdown correction and real-service qualification.
This feed and its original evidence remain unchanged; the statements below
describe its original qualification scope.

Version: `0.0.0-local.20260912.qualified.1`.

Feed: `C:\Users\meisa\.codex\worktrees\9851\FluxFlow\artifacts\core-refactoring\local-candidate-20260912-qualified\feed`.

This includes the HTTP context and optional Engine shutdown corrections alongside
the earlier broker-context correction. Local package gates passed; real-service
integration remains unrun. This is not publication or FluxMQ-NG approval.

| Gate | Verified result |
| --- | --- |
| Manifest, coordinates and archives | 81 packages, 81 symbol archives; all internal dependencies select this version |
| Build identity | 162 target DLLs and symbols match qualified Release output; 81 packaged READMEs match current files |
| All-package consumers | Fresh isolated restores of all 81 packages on each target; archive identity checked; all 162 copied DLLs match |
| Workflow/resource/health/fluent/storage | Ten commands pass, including independent durable seed/recovery processes |
| Minimal Core/Fluent | Four consumers, twelve commands; exact dependency closures, direct Add, reference-identical conditional fanout and independent shared-trace replies |
| Broker context | Nine existing cases per target, eighteen passes; forwarding simulated |
| HTTP context | Ten existing cases per target, twenty passes; four per target use real loopback HTTP, six cover queued contexts |
| Shutdown | Four stop/fault smoke processes, two per target; 128 terminal requests; exact settlement checks and four once-disposed components collectible at the first scheduled check |
| Current source checkpoint | 1,492 inputs unchanged; existing 4,864 regression passes, 76 nonempty suites and five explicitly excluded suites |
| Historical feed | All 81 context.1 package and 81 symbol hashes remain unchanged |

All **36 qualification commands** passed across net8.0/net10.0. The 38 context-test
executions and four shutdown processes rerun existing scenarios, not additional
unique solution tests or new performance measurements. The separate fresh
in-process acceptance pass remains in the [reconciliation](2026-09-12-final-acceptance-reconciliation.md).

The [acceptance record](../../artifacts/core-refactoring/local-candidate-20260912-qualified/acceptance.json)
has SHA256 `703AB3F6E007631513B2346B87CB43A8277CBFB71D10E9E72D32E67389BDFBBB`.
Linked proofs retain commands, logs, hashes and target-specific results. Consumer
projects have no project references or project-type restore libraries. Linked
fixture source is code; production dependencies come from candidate packages.

## Source and assembly delta

Exactly eight qualified inputs differ from context.1: four production C# files,
two permanent test files, the API baseline and Engine README. Twenty target DLLs
change across ten packages: RequestReply, Http.AspNetCore, Engine and its seven
durability/health dependents. Dependent source files are unchanged; package/project
references establish the Engine dependency. All current DLLs match the qualified
build and fresh consumers; this does not assert IL equivalence. The other 142 DLLs,
including both Core binaries, are byte-identical.

The first final verifier expected only the three directly edited packages and
rejected the larger delta after archive/consumer checks passed. The
[inventory correction](../../artifacts/core-refactoring/local-candidate-20260912-qualified/INVENTORY-NOTE.md)
records inspection. The final verifier asserts the exact eight-input/twenty-DLL
sets. No production source or feed changed to satisfy the inventory check.

## Handoff and remaining decision

The [minimal consumer](../../artifacts/core-refactoring/local-candidate-20260912-qualified/minimal/Program.cs)
is the executable direct-API example. The [migration guide](../46-code-first-major-migration.md)
covers package/namespace moves, canonical message ports, explicit reply/discard
policies and portable conditions. The [handoff](2026-09-11-refactoring-handoff.md)
links architecture, features, behavior, performance and limitations.

An authorized read-only check still reports the configured local container-engine
pipe absent. External smoke opt-in, broker host and database connection settings
are absent. Existing container workflow and server-database suites remain unrun.
They require a running local engine or an explicitly configured test environment;
database container scripts additionally require explicit license acceptance.
No service was started and no license accepted.

Local implementation, reconciliation and package qualification are complete.
The remaining integration gate needs environment/authority input. Do not reopen
completed local work or generate more benchmarks while waiting for that decision.
Deferred context-aware Add/templates/iteration remain deferred. FluxMQ-NG task
coordination and publication still require separate authorization. Nothing was
published, tagged, committed, pushed or sent to another task.
