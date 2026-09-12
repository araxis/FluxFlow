# Refreshed local candidate qualification

Candidate `0.0.0-local.20260911.t1400` now includes the qualified ambient-restoration
fix. It supersedes `t0930` for current local evaluation; the older feed and all its
evidence remain unchanged. Nothing was published or sent to an external consumer.

Evidence: `artifacts/core-refactoring/local-candidate-20260911-1400`.

## Provenance and archive checks

All 81 manifest packages and 81 symbol archives were packed without rebuilding.
Every internal dependency uses the same candidate version. Package metadata has
the local version; assembly identities remain those of the qualified build.

The pack and consumer checks retain exactly the same 1,485 qualification inputs as
`ambient-restoration`: 4,828 selected local regression passes, 81 reports, 5,038
discovered cases and a clean nonincremental Release build. That suite was not
rerun here. Its acceptance/container/external/provider exclusions still apply.

`verify.ps1` rechecks all archive hashes and compares all 162 packaged assemblies
and symbol entries to their qualified Release outputs. All 162 assemblies copied
into the two consumer outputs also match. `inventory.json`,
`symbol-inventory.json` and `final-verification.json` retain the evidence.

## Fresh package-only consumers

For each of net8.0 and net10.0, `consume.ps1` creates a fresh project and isolated
package cache. Internal package source mapping is restricted to this feed. The
restored dependency graph contains all 81 expected candidate coordinates, no
project-reference libraries and no other cache. Restored internal archives match
the feed hashes. The acceptance scenario source is copied unchanged.

Both builds finish with zero warnings and errors. All ten commands succeed:
restore, build, ordinary acceptance, durable seed and separate-process recovery
on each framework. Verified scenarios include:

- Document execution, unchanged definition and rejected-update retention.
- Code-first conditional linking, resource-dependent output and health readiness.
- Fluent source/transform/sink execution and exact output.
- Local durable input/output operations.
- Restart recovery of expired input/output leases, captured workflow output,
  resumed pending output and exactly two idempotent destination effects.

Recovery checks use the fixture's explicitly idempotent destination and controlled
clock. They do not establish exactly-once effects for arbitrary external systems.

## Remaining scope

This closes the fresh coherent local-candidate check for the current qualified
source state. Restoring all packages is not behavioral coverage of every module,
nor proof that a minimal Core-only consumer has no optional dependencies.

The wider current-runtime performance/retention matrix, optional host and
telemetry integration boundaries, provider prerequisites, final capability and
structural-convergence audit, and migration/documentation handoff remain separate.
The Core readme still contains historical wording about separate hosted fanout;
it needs a source-backed documentation pass. It was not edited during this frozen
candidate qualification. Final release readiness is not claimed.

External consumer evaluation and publication require separate authorization.
