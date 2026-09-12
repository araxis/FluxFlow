# Local candidate after explicit output policy

Candidate: `0.0.0-local.20260911.t1430`. Local only; nothing published.

The new feed includes the explicit Fluent output-policy fix and current execution
ownership documentation. Older feeds and their evidence remain unchanged.

## Verified boundary

| Gate | Evidence |
| --- | --- |
| Package set and version closure | 81 manifest packages; every internal dependency uses the same local version |
| Symbols and compiled targets | 81 symbol archives; 162 DLL/PDB target entries match Release build |
| Current documentation | All 81 packaged readmes match their source files |
| Full-package consumers | Both targets restore all 81 packages from fresh isolated caches; all archive and 162 consumer DLL hashes match |
| Representative behavior | Ten successful commands covering restore/build, workflow/resource/health/fluent/storage and separate-process restart/recovery on net8/net10 |
| Minimal execution | Four Core/Fluent package-only consumers; twelve successful commands, exact dependency sets and expected output |
| Repeat check | Twenty additional corrected Core executable runs, ten per framework |
| Fresh full build | Nonincremental Release solution build; zero warnings/errors |
| Source qualification | 1,486 stable inputs linked to the output-policy checkpoint: 4,839 local passes and 5,049 discovered cases |

The restored minimal closure is Core plus the dataflow package on net8.0, Core
alone on net10.0 where that assembly is runtime-provided. Fluent adds only its own
package to each closure. No Engine, Composition, Json, designer or provider package
is required by these minimal consumers. Package versions are local metadata
overrides; assembly identities remain those of the qualified build.

## Failure found and corrected

The first attempts failed during SDK configuration access, before application
execution. Approved retries used fresh directories and isolated caches. The first
minimal Core run then timed out waiting for standalone output on net8.0.

The fixture sent input before attaching its transient receiver. FlowOutput
snapshots current links at acceptance and deliberately does not replay messages
to late listeners. Processing could finish in that gap. Current source and the
existing no-subscriber unit test establish this contract; the consumed Core DLL
hash exactly matches the previous candidate, so this is not a new Core binary.

The new artifact-local fixture attaches a capacity-one native output target before
input. It retains all three exact transformations and message/causation checks,
checks for extra output and waits for propagated completion. No production code,
timeout increase, assertion removal or retry-until-pass loop was used. The failed
fixture and older snapshots remain intact. The corrected minimal suite and twenty
additional Core executions pass. Assertion/gap review influenced the attachment
repair and added no-extra-output check; see .testagent/minimal-attachment-review.md.

## Reproducible evidence

Under artifacts/core-refactoring/local-candidate-20260911-1430:

- pack.ps1, inventory.json and pack-verification.json establish package closure.
- consumers-verified-verification.json records the successful full-package runs.
- minimal/verified/core-net8.0-run.log retains the original timeout.
- minimal/attached/verification.json records the corrected four-consumer run.
- repeat-results.json records the twenty additional Core executions.
- final-verification.json, symbol-inventory.json and readme-inventory.json record
  archive/build/document identity; acceptance.json ties the complete checkpoint together.
- solution-build.log records the fresh clean build after consumers.

No new solution tests were added and the full regression was not rerun in this
package-only turn. Its existing qualification is linked by unchanged sources and
binary identity. The repeated runs are a fixture stability check, not a throughput
benchmark or proof of all scheduling interleavings. Restoring all packages is not
behavioral coverage of every optional module. External provider, broader capability,
telemetry and performance gates remain open; the overall goal is not complete.
