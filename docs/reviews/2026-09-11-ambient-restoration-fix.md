# Finished ambient activity restoration

## Change and regression evidence

The existing restoration helper now maps an already-stopped saved activity to
null before assigning the ambient activity. A still-running saved activity is
restored normally. The existing callback exception boundary remains unchanged.
This is one guarded assignment plus an explanatory comment: no public API,
dependency, registry, additional scope or compatibility layer was added.

Before the change, the new regression cases reproduced nine failures per runtime
at the first-chance error-count assertion; all nine live-parent controls passed.
Afterward, all 101 processing-trace cases per runtime pass, including the eighteen
new cases. Failed pre-fix reports remain under `ambient-restoration/before`.

| Requirement | Evidence |
|---|---|
| Suppression across async context; restore live saved activity and avoid errors for stopped saved activity | `Suppression_restores_only_running_ambient_activity` |
| Single/aggregate processing, off/None/propagation/full sampling, correct parent/link and scope lifecycle | `Processing_restores_only_running_ambient_activity` |
| Existing library contracts and API baseline | Clean nonincremental Release build; 4,828 local passes in 81 reports; 5,038 discovered cases |

The tests deliberately observe first-chance invalid-operation errors originating
from Activity: the runtime swallows the invalid-assignment exception, so an
escaped-exception check would not detect the original issue. Stopping the saved
activity in a child task preserves the caller's independent processing context.
Live restoration, actual input parent/link, stop state and observer identity are
checked alongside the zero-error condition. The nonparallel listener collection
and finally cleanup prevent test listeners from leaking.

The testing workflow's assertion and static mutation reviews are recorded in
`.testagent/ambient-restoration-review.md`. The two methods contain sixteen
assertion sites; no assertion-free/trivial-only tests. Removing the guard is
actually demonstrated to fail; other listed mutation outcomes are static
reasoning, not a measured mutation score or coverage percentage.

Full qualification preserves 1,485 source/configuration inputs. Every new case is
verified in regression results and discovery, not merely counted globally. The
public API baseline is unchanged. No external/container suites are silently
claimed by the selected local regression command.

## Matched deep-workflow measurement

Four count checks and sixteen timed runs pass. Four alternating ten-second pairs
per runtime compare the frozen pre-fix Core against the qualified new Core. Only
Core DLL/PDB differ; the harness, dependencies, settings and payload are identical.
Both variants retain the same full-recording counting listener. No build, test or
other diagnostic workload ran concurrently with these measurements.

| Runtime | Median messages/s before → after | Paired median throughput change | Paired median bytes/completed change | Median p99 upper bound before → after |
|---|---:|---:|---:|---:|
| 8.0.30 | 12,523 → 69,702 | +453.02% | -30,891 | 72.26 → 21.60 ms |
| 10.0.11 | 35,236 → 71,494 | +104.10% | -8,553 | 44.42 → 19.60 ms |

All eight pairs improve throughput and reduce allocation and CPU time per
completion. Throughput changes range +445.33% to +465.36% on net8, +96.64% to
+120.24% on net10. Paired median CPU time per completion falls 85.69% / 37.07%.
Heap-delta medians fall slightly, but are not a retention or leak-freedom proof.

The workload is direct, 32 hops, eight reliable targets, capacity 16, four producers,
64-KiB shared immutable JSON, fixed parent and 64 warmup messages. Every run
retains exact sampling/start/stop/full-data/parent totals, all-target delivery,
admission accounting, bounded tracking and queue/handler drain. All 1,485 inputs
and 244 before/after runtime-file entries remain stable throughout measurement.
No affinity, priority, sampling or delivery setting was changed to obtain the gain.

These are descriptive repeated short-run measurements, not confidence intervals,
workload-independent speedup promises or numerical SLO acceptance. Allocation
includes the harness and listener. No exporter, backend, optional update host,
broader payload/condition matrix or long-retention workload is measured here.
The previous traces identify the old path; this slice does not claim a new
post-fix trace census proves zero invalid restoration under every host race.

Raw ordered runs, all paired differences, payload/runtime hashes and validation
are under `ambient-restoration/measurements`. The bounded optimization is verified;
the complete performance and release gates remain open.

## Limits

The guard prevents restoring an activity already known to be stopped. It does not
make the check and assignment atomic against a host concurrently stopping that
same activity. Broader callback discovery/current-change, transport/durable/
revision contexts, SDK/exporter/log/metric collection and long-retention/load
acceptance remain separate requirements. No tracing or delivery feature was
removed. No publishing or external consumer coordination occurred.

Qualification evidence: `artifacts/core-refactoring/ambient-restoration/verification.json`,
command/build/test logs, focused and pre-fix TRX reports. Matched performance
results are recorded separately under the same directory's `measurements` folder.
