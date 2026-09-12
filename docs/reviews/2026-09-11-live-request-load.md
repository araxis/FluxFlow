# Requests during live compatible revisions

The unchanged startup.1 package candidate passed two final smoke processes and
four measured processes across net8.0/net10.0. Measured runs completed **2,426,077
requests and 48 compatible revision replacements**. All replies matched; all 52
component instances drained, disposed exactly once and became collectible after
host disposal. No runtime code or package changed.

## Workload and evidence

The [fixture](../../artifacts/core-refactoring/live-request-load-20260911/Program.cs)
uses the public optional host and stable application ports, eight sequential
request producers, scalar string values and input/output capacity 16. Each process
has 64 untimed warmups followed by ten seconds of requests. Twelve replacements
are paced at 500 ms; workers must still be live and every revision must process
measured requests. The initial revision must process more than its 64 warmups.
Fixture tracking is fixed histograms, eight producer counters and thirteen node
summaries/weak references, not a retained entry per request. No SDK/listener is
installed. The request deadline is a 15-second safety bound, not an SLO.

The [runner](../../artifacts/core-refactoring/live-request-load-20260911/run.ps1)
checks package-only restore, exact archive versions/hashes and ten copied production
DLLs against the qualified candidate. All 1,488 source inputs, five fixture inputs
and 40 runtime files stayed unchanged during each final batch. Consumer builds and
the final full nonincremental solution build were clean: zero warnings/errors.
No additional discoverable solution unit tests were added; the existing local
regression checkpoint remains 4,848 passes, not inflated by these console cases.

Final evidence: [smoke verification](../../artifacts/core-refactoring/live-request-load-20260911/smoke-20260911-214213/verification.json),
[measured verification](../../artifacts/core-refactoring/live-request-load-20260911/measure-20260911-214256/verification.json),
[reproducible summary](../../artifacts/core-refactoring/live-request-load-20260911/summary.json),
[assertion review](../../.testagent/live-request-load-review.md).
The summary verifies all six report hashes. Its SHA256 is
`358097988466547C27884C87D9F7C1E0BF711DBDA7B3C93977C46A2673BFEACA`.

## Measurements

Runs were serial, in the order shown, with opposite framework order in the second
pair. Rates include final request drain. Request latency includes the consumer
call and its reply validation. Apply latency includes candidate construction,
commit and retirement as measured around the public call (definition construction
also occurs within that interval). Histogram percentiles are bucket upper bounds.
With only twelve updates per process, update p99 is effectively the maximum bucket,
not a statistically stable tail estimate. These are local observations, not an
application throughput promise or a framework ranking.

| Run | Completed | Requests/s | Request p99 ms | Apply p99 ms | Allocated bytes/request |
| --- | ---: | ---: | ---: | ---: | ---: |
| net10 first | 609,307 | 60,847 | 1.595 | 48.392 | 22,111 |
| net8 first | 602,921 | 60,239 | 1.327 | 38.132 | 23,049 |
| net8 second | 613,428 | 61,321 | 1.285 | 45.841 | 23,034 |
| net10 second | 600,421 | 59,965 | 1.447 | 55.708 | 22,289 |

Allocation includes typed request projection, reply formatting/validation, the
fixture, optional stable ports, component execution and revision construction.
It is substantial and is not presented as Core-only cost or a measured regression
against an original host baseline. This result does not erase the separately
reported standalone before/after delivery regression. Further optimization needs
attribution and a concrete benefit, not new machinery solely to increase rate.

All 52 measured weak probes cleared on the first post-disposal checkpoint, taking
23.9–33.6 ms including the 20-ms async boundary and full collections. The two final
smokes separately completed 131,802 requests, eight updates and ten clear probes.
Warmups and smoke counts are excluded from measured request totals.

## Behavior and limits

`Sustained_requests_survive_live_revisions` verifies exact producer/sequence reply
data, preserved trace and non-null exchange identity; stable port facade and exact
committed revision IDs; completed producer totals equal latency sample totals;
all node processing equals replies plus warmups; nonempty public port status with
zero pending messages; captured completion succeeds and each node is disposed once.
`Disposed_revision_components_are_collectible` checks every component weak probe
after the disposed workload returns and an async observation boundary.

This closes representative successful compatible-update request load and selected
post-disposal component retention. It does not prove private pending-registry
counts, long-lived host retention, every queue peak, incompatible port changes,
terminal fault load, or all observer/SDK ownership. Existing controlled in-flight
tests separately cover startup rejection/cancellation and held-old/new routing;
they are not relabeled as sustained failure load. No numeric SLO is imposed.

Initial restore failure (`smoke-20260911-214009`) was a configuration access
restriction. The authorized retry (`smoke-20260911-214058`) passed, then review
strengthened initial-revision activity to exclude warmup-only progress. Both are
preserved but excluded from final evidence. Final smoke and measurement use the
strengthened source. No production fix was needed, nothing was published, and
the overall acceptance goal remains open.
