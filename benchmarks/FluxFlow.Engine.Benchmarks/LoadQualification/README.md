# Bounded runtime load qualification

The `--load` command is separate from microbenchmark jobs. It runs an ordinary Core
application or actual update hosting with the same forwarding components, terminal
sinks, payload and capacities. It does not replace production admission/routing with
a benchmark queue. By default no telemetry listener is installed. Optional tracing
modes install bounded counter callbacks, not a logger, exporter or backend; external
instrumentation must still be disclosed separately.

```powershell
dotnet run --project benchmarks/FluxFlow.Engine.Benchmarks -c Release -f net10.0 --no-build -- --load --runtime direct --seconds 30 --hops 8 --fanout 2 --capacity 16 --producers 4 --payload-bytes 4096 --slow-target-ms 2 --output artifacts/load/direct-slow.json
```

Repeat with `--runtime updates`, using a different output path. Build both target
frameworks first and use `-f net8.0` for that runtime. Existing output files are never
overwritten. `--messages N` substitutes a count-limited run for smoke/tests. Unknown,
duplicate and out-of-range options fail. The command has a three-minute safety
deadline; unusually large capacity/delay combinations can exceed it and fail rather
than yield an incomplete passing report.

## Workload and correctness

- Each producer creates a distinct canonical envelope over one immutable shared
  value, with a common business trace ID. `--payload-kind json` (default) uses an ASCII
  data string; `--payload-kind binary` uses deterministic exact content carried inside
  the same canonical value. `payload-bytes` is the ASCII string length or raw binary
  content length, not the full serialized wire size (binary content is base64 encoded).
  Payload materialization
  is setup, not forwarding work. The receiving sink validates original-envelope identity.
  `PayloadSha256` fingerprints the actual shared value's UTF-8 JSON representation
  once during setup, so matching workloads can verify payload identity independently
  of the requested configuration. Hashing is outside the measured interval.
- Every selected fanout sink must receive every admitted envelope once. The first sink
  can delay each operation to exert real backpressure; others remain fast. Delays use
  the runtime scheduler, so timer resolution affects their actual duration.
- Tracking is registered before admission, then removed after all target receipts or
  canceled/rejected admission. Fast delivery before the send returns is supported.
  Tracking has a hard limit derived conservatively from component and optional-host
  queue capacities plus producer attempts. Histogram storage is fixed, independent of
  throughput, and at most 512 periodic samples are retained.
  Each update-host forwarding hop includes three additional bounded queues: stable
  input admission, prepared-output staging, and stable-output dispatch. The public
  optional-port backlog snapshot does not expose prepared staging; exact all-target
  completion accounting remains the end-to-end drain check.
- After 64 warmup messages, production runs for the configured interval. Its end cancels
  pending producer admissions; already admitted work drains naturally. The harness
  waits for zero tracking, native component input queues, active handlers and (for
  updates) exposed business-port pending counts. Only then does it stop/dispose the graph.
  This is not shutdown-with-in-flight-work or revision-swap qualification.
- The command fails if attempt/admission/completion/target totals disagree, a duplicate
  or changed envelope appears, an unexpected rejection occurs, the tracking bound is
  exceeded, or accepted work does not drain. It never emits a passing partial report.

## Reading results

`--tracing off|unsampled|propagation|recorded` selects no listener, sampling None,
propagation-only spans or full recording. `--input-trace-context true` supplies a fixed
valid W3C parent to every input, identically in each comparison mode. The default is
false. This measures pass-through processing with unchanged envelope identity, not
new producer spans or derived carriers on every hop. Parent correlation is checked
on every observed operation when input context is enabled.

The Tracing report counts sampling, started/stopped/full-data/correlated operations,
peak active scopes and active scopes after drain, including warmup. Counts must equal
the configured hops times all delivered and warmup messages, adjusted for sampling.
The harness fails on mismatch and retains no activity objects. Timed allocation/CPU
figures include the counter callbacks; they are not exporter cost. Idle waiting also
waits for active observed scopes, while final count validation follows graph stop.

Admission and all-target latency are recorded per envelope using the monotonic clock,
starting immediately before admission/tracking. They exclude payload creation and
graph startup. Percentiles are logarithmic bucket **upper bounds**, with 64 buckets
per doubling (roughly 1.1% relative bucket width away from zero), not exact order
statistics or percentiles of batch means. All-target duration includes backpressure
and the final natural drain. Completed/second uses that entire measured interval;
producing time and drain time are also reported separately.

Allocation counts cover the entire process workload, including envelope construction,
tracking, sampling and runtime work. Synchronous sampler allocations are separately
reported because the optional host exposes richer status than native component blocks.
Neither figure is a pure per-hop library allocation claim. No CPU affinity, process
priority, GC-mode change or power-plan change is made.

Managed-heap baselines use forced collection after warmup and again after idle drain,
while the graph and bounded sample storage remain allocated. Periodic samples do not
force collection. Dictionary capacity and legitimate runtime caches can remain after
drain; a heap delta alone is not a memory leak. Private bytes include process/runtime
reservation and are not expected to return to the initial value. Compare repeated
matched workloads and longer plateaus before drawing retained-memory conclusions.
Queue and progress samples are point observations, not atomic global snapshots.

This initial harness covers forwarding, slow-target saturation, producer-admission
cancellation and idle recovery. Request exchanges, actual revision swaps, all evaluator
modes, exporter/log/metric collection and the full payload/topology matrix remain separate required
gates. Numeric budgets must follow measurements; this command does not invent an SLO,
prove complete runtime convergence, or qualify a package release.
