# Diagnostic publication: matched workflow measurement

The internal typed-publication change has a measurable benefit in the tested
revision-enabled workflow. All sixteen timed before/after pairs allocate less and
complete messages faster. This qualifies this change for these workloads, not the
complete performance or release gate.

## Comparison and verification

The before Engine DLL/PDB comes from the frozen capacity-qualified workflow run.
The after Engine DLL/PDB comes from the subsequent diagnostic-qualified build.
Both variants use the same current benchmark, Core and other runtime dependencies.
Only Engine DLL/PDB differs; portable symbol identities are checked against their
assemblies. The benchmark source is unchanged. Its rebuilt binary is shared by both
variants rather than mixing historical and current benchmark binaries.

Source comparison permits exactly the two diagnostic producer/publication files
and their observer test file. The prior full build and 4421-case local regression
qualify this current source; no production or test code changed during this run.
All 1481 inventoried source inputs and 244 runtime files remain stable.

Sixteen 256-message smoke runs cover direct and revision-enabled graphs on both
frameworks and both sizes. Thirty-two timed runs cover revision-enabled graphs,
four alternating before/after pairs per size/framework. Each fresh process has
64 sequential warmup messages, a ten-second producing interval, four producers,
capacity 16, and a shared 64-KiB JSON payload. Small means one forwarding hop and
one target; deep means 32 forwarding hops and eight targets. No slow target or
telemetry listener is configured. Affinity/priority are unchanged.

Every run passes exact admission/completion/target counts, payload fingerprint,
bounded tracking, zero business queue/handler/stable-port backlog after drain and
process completion checks. Session 66107 completed with exit zero. All reports and
command arguments are retained under artifacts/core-refactoring/typed-diagnostics/load.

## Results

These are medians of four paired percentage or byte differences, not percentages
computed from independent medians. Allocation includes the harness.

| Framework / shape | Throughput median | Pair range | Allocation median | Bytes saved/completion | p99 upper-bound median change |
|---|---:|---:|---:|---:|---:|
| net8 / small | +13.17% | +10.86% to +15.38% | -14.39% | 3497 | -22.05% |
| net8 / deep | +26.49% | +23.14% to +35.61% | -16.72% | 95777 | -17.25% |
| net10 / small | +15.02% | +5.52% to +16.26% | -14.69% | 3479 | -14.47% |
| net10 / deep | +24.22% | +15.62% to +29.45% | -18.33% | 100931 | -19.71% |

All sixteen pairs improve throughput/allocation; fifteen improve the p99 bound.
Four repeats are descriptive evidence, not a significance test. Latencies are
per-message logarithmic bucket upper bounds and include admission/all-target
completion; they are not microbenchmark iteration percentiles.

Absolute after medians remain significant under deep saturation: roughly 475 KB
allocated/completion and 1125 ms p99 upper bound on net8, 450 KB and 1049 ms on
net10. This does not establish an acceptable latency budget. Forced-GC retained
heap deltas are mixed, and business drain is not complete observation quiescence;
do not claim reduced retained memory or leak freedom.

Authoritative outputs: results/verification.json, results/comparison.json,
results/timed-details.json, results/descriptive-details.json, results/runs.json,
results/binary-inventory.json and each workload's raw JSON/log. Preparation and
runner scripts preserve earlier checkpoints and reject overwriting their results.

## Decision and remaining work

Keep the typed internal publication path. It removes an encode/decode roundtrip
without changing canonical transport, adding a queue or weakening observer and
validation contracts. Do not repeat this comparison without a new change or a
specific contrary result.

Next address operation tracing rather than accumulating more measurements of this
same optimization. Current source inspection confirms FlowApplication.RequestAsync,
ApplicationPortRuntime.RequestAsync and the standard RequestReplyCoordinator path
call FlowRequestReply.RequestAsync. That Core method owns bounded exchange startup,
admission, reply waiting and cleanup but does not create an operation activity.
The shared location is the next boundary to qualify; this is not a claim that all
transport/custom request paths have been traced. Inspect the actual context and
lifetime contracts before adding instrumentation, without another coordinator.

Enabled telemetry, transport/fan-in/source operation coverage, cancellation/revision
load, longer retention, feature-ledger completion and a refreshed coherent local
candidate remain separate gates. The earlier feed predates this change. No package
was published and no external consumer work was coordinated.
