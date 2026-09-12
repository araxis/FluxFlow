# Loaded optional-host termination

Status: shutdown error-preservation correction and selected loaded termination
scope qualified. No new package candidate or publication.

## Final qualification

**4,864 local regression tests passed**, 81 reports: 76 nonempty suites and the
same five explicitly excluded external suites. Fresh nonincremental Release
solution build: zero warnings/errors, 19.18 s. All **1,492** inputs unchanged.
This is executed-suite accounting, not fresh discovery of every external case.

Four final smokes and eight measured source-linked processes pass on net8.0/net10.0:
**5,137,288** timed concurrent-load replies across **32** host lifetimes, followed
by **1,024** held terminal requests. All 512 graceful-terminal replies are exact;
all 512 fault-terminal requests settle without success or timeout. Every component
disposes once and all 32 weak probes clear at the first scheduled collection.
The cases are `Loaded_host_settles_stop_or_fault` and
`Terminated_components_are_collectible`; the permanent three-case theory is
`Stop_preserves_component_failure_when_input_drain_also_fails`.

| Framework/mode | Timed replies | Requests/s range | p99 upper-bound range (ms) | Terminal burst/cleanup range (ms) |
| --- | ---: | ---: | ---: | ---: |
| net8 / stop | 1,292,361 | 33,134–79,514 | 1.057–1.495 | 0.879–33.592 |
| net8 / fault | 1,238,993 | 32,990–76,331 | 1.068–1.511 | 1.205–26.430 |
| net10 / stop | 1,311,307 | 34,955–85,840 | 1.128–1.495 | 0.719–40.066 |
| net10 / fault | 1,294,627 | 34,858–84,898 | 1.116–1.648 | 0.555–32.871 |

Ranges span the eight lifetimes in each framework/mode, not confidence intervals.
Across all lifetimes, p50 upper bounds are 0.068–0.210 ms, p95 0.116–0.342 ms;
process allocation deltas divided by successful requests are 21,312–22,420 bytes.
These include typed projection, dynamic authoring host and fixture/request work,
not only Core delivery. Timed intervals begin after host startup, with no discarded
warmup: early process JIT/tiering is included. The wide throughput spread is not a
before/after improvement claim or a capacity/SLO promise. Raw per-lifetime values
and exact commands/settings are retained in the verification record.

[Regression verification](../../artifacts/core-refactoring/host-termination-load-20260912/final-qualified/verification.json)
SHA-256: `FE7FDC8B9E9286FF1974BF51C0BDD1201E99DBBCD485CB00FE35C1C71FAE6489`.
[Load verification](../../artifacts/core-refactoring/host-termination-load-20260912/load-race-qualified/verification.json)
SHA-256: `C280C5F86E542DBD77E79EAB41A1F2B8341C52A49C0957C0373C7CC33E0F53D7`.
All ten consumed production DLLs match current Release builds; only the two Engine
DLLs differ from this dependency closure's frozen context.1 binaries. Core is
unchanged. All four fixture inputs and 36 runtime files remain stable. The
[audit](../../artifacts/core-refactoring/host-termination-load-20260912/audit.json)
also verifies all ten frozen reproducer DLLs and five package archives against
the old candidate inventory. HTTP corrections remain outside this closure.

An intermediate race-qualified regression passed tests but correctly refused a
verification record because the Engine README changed during execution. Final
qualification above reran after documentation settled; that earlier attempt and
the insufficient first fix's failed measured run remain preserved.

## Defect and correction

The frozen context.1 package's graceful-stop smoke drained its held request burst.
Its fault smoke settled requests but reported only an input-drain exception at
StopAsync, omitting the original component fault. The permanent regression
`Stop_preserves_component_failure_when_input_drain_also_fails` reproduced this with
32 requests. A first correction included the lifetime's first observed error and
passed two cases plus a 4,863-pass local regression, but a longer net10.0 workload
exposed an observation race. That intermediate result is not final qualification.

The third regression case deliberately delays the component completion observer
until shutdown starts. This makes the secondary drain error win the observation
race; the case failed on the first correction and passes on the revised one. It
asserts the original exception object appears exactly once and that the specific
secondary input-drain error also remains visible. The single-request case checks
the unqueued path. Finally releases the test gate and unfinished processing.

`ApplicationRuntimeRevisionCandidate.DrainAsync` now uses the existing shared
lifetime to fault owned work, awaits component termination, and gathers settled
component failures alongside the drain error. Aggregates are flattened and duplicate
exception references removed. This changes only the optional Engine error path;
Core, public APIs, dependency boundaries and successful routing are unchanged.
It adds no failure coordinator. As before, component fault callbacks must release
their work: candidate disposal already waits for component termination.

## Workload and oracles

The [fixture](../../artifacts/core-refactoring/host-termination-load-20260912/Program.cs)
uses eight concurrent producers per timed interval, four-capacity public/node
inputs and outputs, and exact scalar string request/reply values with a shared
business trace. After the producers settle, 32 requests are deliberately held at
the processing boundary. Graceful stop must wait, then complete all replies when
released; forced component failure must settle all requests without waiting for
their deadlines or returning success. Owned disposal is exactly once.

The public facade becomes unavailable when its runtime is released. Completion
is therefore captured before stop, rather than querying post-release status.
Positive public queued work is checked before termination; final completion and
request/processing counts prove settlement, not a sampled private queue count.

Latency uses the existing fixed-size histogram. Timed request latency and terminal
burst/cleanup duration are separate; per-request payloads are not retained for
measurement. One weak component probe per lifetime must clear within eight full
collection attempts with 20-ms scheduling intervals. No whole-heap leak claim.

Final scope: four smokes and eight fresh measured processes (two repetitions
per termination mode/framework), four 2.5-second loaded lifetimes per measured
process. This is load followed by termination under queued work—not arbitrary
external ingress racing continuously with shutdown, installed SDK overhead, or a
numerical application SLO. No exhaustive Cartesian matrix is required.

The corrected fixture uses source project references. The package-only project
and its failed frozen-package run remain separate reproducer evidence; neither
is relabeled as final package acceptance. See the
[assertion review](../../.testagent/host-termination-review.md) for exact assertions,
fixture corrections and reasoned mutation checks. No coverage percentage or
actual mutation-engine result is claimed.

This closes the representative loaded optional-host stop/fault scope. Timed SDK
overhead, cross-cutting acceptance reconciliation, available real-service gates
and a refreshed coherent candidate remain. Nothing was published or sent to an
external consumer task.
