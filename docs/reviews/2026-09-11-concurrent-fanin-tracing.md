# Concurrent fan-in tracing qualification

Two upstream outputs can feed one component without combining messages. The
existing Core processing wrappers already preserve the right per-message context
when their delegates overlap. This checkpoint adds deterministic proof instead of
another runtime layer or per-component telemetry state.

One parameterized test covers both wrappers, shared/independent traces and all four
listener/sampling modes: Concurrent_fanin_keeps_each_message_context_across_await.
Both callbacks are held before either returns; reverse release verifies independent
execution. Assertions cover input/output identity, exact causal links and parents,
distinct span IDs, activity across awaits, derived carrier and values, disabled
header identity, tag sampling, complete drain and ambient restoration. It generates
32 cases across net8/net10. Existing incoming-error bypass proof was also reopened
and rerun; it is not confused with handling failures on already-error data.

## Evidence

- Focused processing suite: 112 passed, zero failed/skipped.
- Nonincremental Release solution build: zero warnings/errors.
- Selected local regression: 4,499 passed, zero failed/skipped, 79 reports.
- Solution discovery: 4,709, including all 32 new cases.
- Qualification inventory: 1,482 stable inputs.

Authoritative artifacts: artifacts/core-refactoring/fanin-tracing/verification.json,
commands.json, build/regression/discovery logs and TRX files. Requirement mappings,
34-assertion review and ten selected static pseudo-mutations are recorded in
.testagent/fanin-tracing-review.md. No measured coverage or mutation score is claimed.
No production source, package reference or public API changed in this checkpoint.

## Next production correction

Independent-message routing is not multi-message aggregation. Messaging guidance
uses links for each message represented by a processing operation; that distinction
informed this review, not a new framework-specific provenance mechanism.
See [consumer correlation guidance](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#consumer-spans).

Current JoinNode has both input envelopes when emitting a matched result, but its
queued command executes without context isolation and the result derives from the
left message without linking both inputs. The next fix should address this actual
operation, preserving left-side business correlation, FIFO/deadlines, bounded
pending state and completion. Do not make every forwarded message retain ancestors.

MetricsAggregateNode has a distinct incremental/history problem: its final snapshot
derives from the last accepted message. That needs a bounded policy, not an unbounded
list of every sample's context. Neither aggregation path is qualified by these tests.

The telemetry skill kept the review at operation/context boundaries; no host SDK,
exporter or dashboard was added. Source/custom/transport/durable/revision context,
discovery callbacks, callback latency, enabled telemetry/load/retention and a fresh
package candidate remain open. No publishing or external consumer coordination.
