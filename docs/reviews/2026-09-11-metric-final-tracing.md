# Coalesced metric snapshot tracing checkpoint

Final coalesced snapshot emission now uses the existing Core processing helper.
It starts a root operation with at most one link to the last accepted input, and
covers snapshot creation, serialization and bounded output admission. Input-update
activities remain owned by the existing wrapper. Downstream consumption is outside
the final operation; no component-lifetime activity or historical context registry.

This is a bounded correlation policy, not all-contributor provenance. Each normal
update is traced with its own input. The final snapshot preserves existing business
lineage from the last sample that contributed to the global aggregate, even when
that sample exceeded the per-group limit. Rejected invalid samples do not replace
that lineage. The correlation input count is one, not the snapshot's sample count.

Using a root with a link is a library design choice consistent with the distinction
between parent context and links in the
[tracing API](https://opentelemetry.io/docs/specs/otel/trace/api/#specifying-links).
The specification does not prescribe this particular sparse-history policy.

## Simplification and ownership

No new caller API, runtime, registry, host/exporter dependency or context list.
The existing typed accepted-message field is retained only in coalesced mode and
cleared after flush or disposal. A null-payload canonical envelope is created once
at final emission to reuse shared tracing and immutable derivation. Keeping the typed
sample avoids retaining unused large fields from the original unmaterialized payload.
The existing aggregation/latest/rate-window state remains unchanged; this is not a
new bound on the rate-window queues or a measured retained-heap claim.

Disabled or unsampled tracing preserves the last accepted input carrier rather than
inheriting completion-worker context. Owned cancellation is recorded as canceled;
declined admission and unexpected failure are errors. Original owner failures still
propagate unchanged. Sample/start/stop listener exceptions do not change results.

The source-declaration baseline includes protected methods, even in sealed classes.
Adding the existing disposal-hook override changes only Metrics row 37 from 89 to 90
declarations. This is an implementation override, not a new callable public API or
consumer inheritance point. It was reviewed as patch-compatible in the unpublished
train; both API baseline tests pass. The original row is preserved in the artifacts.

## Requirement evidence

| Requirement | Exact test |
|---|---|
| R1: sampling, bounded links, roots, carrier/lineage and rejected tail | Final_snapshot_uses_one_bounded_link_not_worker_or_history_parent |
| R2: no unnecessary final scopes/results | Streams_without_a_final_snapshot_do_not_start_a_final_scope |
| R3: observer failure containment | Listener_failure_does_not_change_final_snapshot |
| R4: admission duration, graceful drain, owner failure and decline | Final_snapshot_scope_covers_admission_and_preserves_terminal_outcome |

The first R1 run failed all 16 net10 cases against the prior implementation. The
disabled cases proved an actual worker-context leak; enabled cases lacked the final
operation. Final focused verification passes 25 new cases per framework and all 51
Metrics cases per framework (102 total). Three final fresh-process repetitions pass
150 additional new-test executions. Metrics tests now exercise net8 as well as net10,
adding 26 existing cases on net8 separately from the 50 new tracing cases.

Final nonincremental Release build: zero warnings/errors. Selected local regression:
4,717 passes in 81 reports, zero failed/skipped. Discovery: 4,927 including all 50
new cases. Stable qualification inputs: 1,484. The delta from the last checkpoint is
76: 50 new tracing cases plus 26 existing Metrics cases newly run on net8.

Final full-workspace qualification is recorded in
artifacts/core-refactoring/metric-final-tracing/final/verification.json. The parent
directory preserves initial red tests, the first full run's baseline mismatch,
focused/final/repeated runs and public-api-before.txt. The assertion review contains
87 assertion call sites and nine selected pseudo-mutations; only removal/absence of
the final scope was demonstrated by executed red/green evidence. No coverage or
measured mutation score is claimed.

The testing skill drove deterministic native-block backpressure, exact exception
identity and negative-path checks. The telemetry skill kept instrumentation at the
actual operation boundary without adding host collection infrastructure.

## Remaining acceptance gates

Selected regression excludes acceptance, container, external smoke and database
integration tests, as recorded in the command. This is not every interleaving or
sampling mode under saturation, complete provenance, source/transport/durable/revision
coverage, listener-discovery/latency qualification or enabled-load/retention evidence.
Those remaining boundaries, performance gates and fresh coherent local packages
remain open. The full refactoring goal stays active. Nothing was published or sent
to external consumers.
