# Recording-path diagnostic capture

## Result

Four focused captures now contain allocation and thread-sample events with zero
reported loss: listener-free and full-recording deep workflows on both supported
runtimes. Frozen Core, Engine and harness binaries match their portable symbols.
This completes capture and raw-event validation, not stack resolution, cost
attribution, optimization or performance acceptance.

| Runtime / observer | Allocation ticks | Thread samples | Reported loss | Capture bytes |
|---|---:|---:|---:|---:|
| 8.0.30 / off | 153,556 | 114,536 | 0 | 31,334,058 |
| 8.0.30 / recorded | 75,189 | 106,165 | 0 | 16,247,658 |
| 10.0.11 / off | 154,646 | 111,602 | 0 | 31,546,598 |
| 10.0.11 / recorded | 200,381 | 56,598 | 0 | 33,975,510 |

Counts are not normalized performance comparisons. Allocation ticks sample bytes
between events rather than count every object, and thread sampling is not exact
CPU time. Collection changes timing and scheduling. Do not compare these run
timings with the preceding untraced observer-isolation or tracing-load results.

## Reproduction and identity

The artifact-local collector launches four separate synthetic processes, using
the existing local collector version
`10.0.745401+cef304c50763bf24f99566cb31d55540842e7ae9`. Each uses the same direct
32-hop/eight-target graph, capacity 16, four producers, shared immutable 64-KiB
JSON, fixed input parent and 64 warmup messages. Production runs for ten seconds
then drains. Full recording uses the existing counting listener; no startup hook,
SDK, exporter, backend or unrelated-process attachment is used.

The focused captures retain sampled-thread-time and GC/allocation events, with an
explicit GC-only runtime keyword override (`0x1`, verbose). The collector logs
verify that override took effect. GC-handle and exception events from the broader
profile are not required for this capture. A 256-MiB buffer is unchanged.

All eight launched workloads (four initial, four focused) exit successfully and
pass exact all-target delivery, admission accounting, input-parent/scope totals,
bounded tracking and business queue/handler drain. Both capture passes preserve
1,485 qualification inputs and 122 runtime files, checked against the preceding
observer-isolation checkpoint and again afterward. Runtime copies remain frozen
for subsequent symbol resolution. Six DLL/PDB pairs match: Core, Engine and
harness on both frameworks.

The focused raw reader successfully parses all four files, checks allocation and
thread samples from one observed process per capture, and reports zero event loss.
It does not perform a census of every event type. The metadata records PIDs,
timestamps, pointer size, processor count and trace hashes. Manifests retain exact
launch commands, runtime/OS/architecture, payload fingerprint and collector/parser
identity. Capture OS is Windows 10.0.26200, with 20 logical processors.

## Failed attempts retained

The original broad profile captures remain under `results`, but are not a
qualified analysis set. The recorded net8 trace reports a nonzero negative
`EventsLost` value (-2,139,225,621). That value is not interpreted as an exact loss
count; it fails the zero-loss gate. The cause of the negative value is unproven.
No attempt was made to label it harmless or attribute costs from that capture.
The focused retry uses the same binaries and workload but fewer runtime events.

Two earlier reader checks also failed: the initial generic reader had not
registered the sample-profiler decoder, and a subsequent check incorrectly
required separate stack-walk events. Registering the decoder exposed actual
`Thread/Sample` events. Separate stack-walk event counts remain zero; the validator
does not equate that with absent or resolved embedded stacks. Converted-stack
validation is still required. All failed reports and the initial collection
script are preserved. There is no successful validation manifest for the original
set, and the retry has its own `focused-results` directory.

## Next action and limits

Use the frozen binaries and existing local trace-analysis library to validate
converted stack availability, then inspect sampled allocation types and managed
inclusive/exclusive stacks. A trace viewer is also suitable. Keep unresolved
frames, overlapping inclusive totals, sample weighting, startup/warmup/drain and
observer work explicit. The trace-collection workflow establishes capture quality;
it does not establish the cause of the recording overhead.

No production source, API, permanent benchmark, package or test changed. The
previous 4,792-pass / 81-report regression and 5,002-case discovery checkpoint
remains the qualification for the identical inputs; it was not rerun this turn.
Remaining operation boundaries, broader performance/retention qualification,
SDK/export/log/metric collection and refreshed local packages remain open.
No publishing or external coordination.

Authoritative evidence is under `artifacts/core-refactoring/recording-trace/`:
`focused-results/collection-verification.json`, `validation-verification.json`,
`trace-metadata.json`, `symbol-matches.json`, ordered `collections.json`, raw
workload reports, collector logs and `.nettrace` files. Reproduction uses
`collect.ps1 -Focused` and `validate.ps1 -Focused`; existing evidence is not
overwritten.
