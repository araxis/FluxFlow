# Sustained saturation qualification

Eight count-limited smoke runs and eight serial two-minute production runs passed
against the existing local candidate. The measured runs completed 65,011 envelopes;
each selected target received every admitted envelope exactly once, preserving
reference identity. All runs ended with zero tracked messages, component queues,
active handlers, exposed optional-host pending work and observed active operations.

This is bounded-backlog and natural-drain evidence, not release approval, peak
throughput, a before-refactor speed comparison or a long-term memory-leak proof.

## Workload and identity

Each run used eight forwarding hops, two targets, queue capacity 16, four producers,
one shared deterministic 64-KiB binary payload, a fixed incoming trace parent and 64
warmup messages. One target requested a 2 ms delay per message. Actual scheduling
made that target's effective service rate roughly 64 messages/second; the requested
delay is not a guarantee of 500 messages/second.

The matrix crosses direct execution / optional update hosting, no tracing listener /
fully recorded processing, and .NET 8.0.30 / .NET 10.0.11. Update hosting starts one
revision; it does not swap revisions. Recording uses bounded counters, not an
exporter. Tracing order is reversed on the second framework, but there is only one
measured run per cell and no claim of statistical replication.

Environment: Windows 10.0.26200, X64, 20 reported processors, workstation GC. No
affinity, process-priority or power-plan changes. No concurrent builds or other load
tests were launched by this task during the measured batch; the machine was not
claimed to be a dedicated benchmark host.

The batch verified 1,487 source inputs and 102 runtime files unchanged. Package
dependencies in the benchmark output matched candidate
`0.0.0-local.20260911.resource.2`. No production, test or benchmark sources changed.
All reports matched payload SHA256
`1A5AB9339E41354EAF5501A08AB6B450DD07E04C2E694CE157F3FEC08853CF84`.

## Results

Rate includes final drain. Latencies are individual-envelope all-target histogram
upper bounds, including admission wait; they are not exact quantiles. These are
deliberately saturated queues, not normal-load response-time measurements.

| Framework | Hosting | Tracing | Completed | Messages/s | p99 upper, seconds | Drain, seconds | Peak tracked / bound |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 8 | Direct | Off | 7,930 | 63.72 | 4.52 | 4.42 | 284 / 300 |
| 8 | Direct | Recorded | 7,922 | 63.66 | 4.57 | 4.41 | 284 / 300 |
| 8 | Updates | Off | 8,322 | 63.69 | 10.88 | 10.64 | 684 / 716 |
| 8 | Updates | Recorded | 8,331 | 63.75 | 10.88 | 10.65 | 684 / 716 |
| 10 | Direct | Recorded | 7,935 | 63.80 | 4.52 | 4.35 | 284 / 300 |
| 10 | Direct | Off | 7,930 | 63.75 | 4.52 | 4.37 | 284 / 300 |
| 10 | Updates | Recorded | 8,321 | 63.68 | 10.88 | 10.65 | 684 / 716 |
| 10 | Updates | Off | 8,320 | 63.67 | 11.00 | 10.65 | 684 / 716 |

Each measured run had three or four canceled producer admissions and no rejected
admissions. Attempts equaled completed plus canceled admissions. Cancellation ended
new production; accepted work drained naturally before graph shutdown.

All four 30-second production windows had sufficient observations. After the first
30 seconds, direct tracking remained at 283–284; update hosting stayed at 683–684,
apart from one cell whose minimum was 682. This supports a stable bounded plateau
for this workload. Update hosting's additional bounded queues allow a larger
backlog and correspondingly longer latency/drain; bounded does not mean low latency.

Recorded processing counts exactly matched eight hops times completed-plus-warmup
messages, with parent correlation checked and zero active scopes after drain. Off
runs reported zero processing callbacks. Similar throughput does not establish
negligible tracing overhead: the slow target limits all cells.

## Memory and interpretation limits

Direct runs retained 498 periodic samples; update runs reached the 512-sample cap
around 128 seconds. Their final drain tail is therefore not fully sampled. Terminal
drain counters are verified separately. The forced-GC heap delta ranged from
−149,232 to +68,768 bytes, measured with the graph and harness storage still alive,
before graph stop. Neither that range nor ordinary heap samples prove absence of
leaks. Raw reports retain whole-process allocation, CPU, p50/p95/p99 and heap data;
those allocation figures include envelopes, tracking, sampling and instrumentation.

The user clarified that the engine serves ordinary applications, not online gaming
or actor-framework benchmark competition. Priorities remain simplicity, predictable
resource use, reliable delivery and measured responsiveness. No unsupported numeric
throughput or latency SLO is inferred from that clarification or this experiment.

Remaining performance evidence includes representative request/reply concurrency
and cancellation, actual revision changes, stop/fault with work in flight, relevant
condition/telemetry combinations, observation-quiescent retention and workload-based
budgets. Existing count-limited matrices and unit tests do not substitute for those
measurements. See `2026-09-11-performance-evidence-coverage.md` for the scope ledger.

## Reproducible evidence

Artifacts are under `artifacts/core-refactoring/long-saturation-20260911`:

- `PLAN.md` and `run.ps1`: workload, exclusions and executable checks.
- `runs.json`, per-case JSON and logs: all 16 successful cases and report hashes.
- `source-before.json` and `binary-inventory.json`: identity checkpoints.
- `verification.json`: completed batch and stable input counts.
- `summarize.ps1`: independently rechecks cell coverage, report/source/binary hashes,
  known payload identity and final accounting, then prints the detailed result rows.

Both the batch and summary verification exited successfully. The existing package
candidate is unchanged; nothing was published or sent to a consuming application.
