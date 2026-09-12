# Join operation tracing checkpoint

The join now uses the same Core processing scope as standard wrappers. Each input
command has its own context for key selection and pending admission. Matched-result
construction and bounded output admission use one aggregate root with links to both
input contexts; the result carries that activity context while retaining left-sided
business correlation, trace ID and causation. Timeout output uses its expired input's
context, including timer and completion-flush commands. Non-message commands suppress
inherited worker context. No per-hop clone or global message/provenance registry.

## Small public extension point

The existing FlowTracing helper is now public, with single-message and span-of-messages
processing overloads, context suppression and its disposable operation scope. Custom
components can reuse the same implementation without friend access or duplicated
instrumentation. Scope construction remains internal. Failed/Canceled/Outcome methods
record operation results; hosts still own listeners, sampling and export.

This is an additive API within the unpublished preview train. Only Core's source API
baseline changes, from 419 to 427 declarations; all other rows are unchanged. Core's
README, processing-context guide and changelog describe the addition. The helper
does not retain the caller's input span or messages. Links are built only with an
interested listener; no quantitative disabled/enabled overhead claim is made.

The multi-message causal model follows the distinction between individual processing
and processing that accounts for several inputs. See the
[consumer correlation guidance](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#consumer-spans).
The telemetry skill was applied to this library operation boundary, without adding
SDK/exporter infrastructure to Core.

## Qualification

- New tracing tests: 38 per framework, 76 total, passing.
- Final focused join/pending-store/tracing suite: 59 per framework, 118 total, passing.
- Routing.Tests now runs net8 and net10, adding its previous 54 tests on net8.
- Public API acceptance: two tests pass; only the intentional Core row changes.
- Fresh nonincremental Release solution build: zero warnings/errors.
- Selected local regression: 4,629 passed in 80 reports, zero failed/skipped.
- Solution discovery: 4,839, including all 76 new tracing cases. The total increase
  of 130 also includes the 54 existing routing cases newly exercised on net8.
- Stable qualification inputs: 1,483.

Authoritative evidence: artifacts/core-refactoring/join-tracing/verification.json,
command logs, TRX files and public-api-before.txt. Exact requirement-to-test mapping,
92-assertion review and twelve selected static pseudo-mutations are in
.testagent/join-tracing-review.md. No coverage or measured mutation score is claimed.

## Remaining gates

The recorded regression filter excludes acceptance, container, external smoke and
database integration suites. This checkpoint does not prove trace outcomes during
held-output cancellation/failure, non-message command error reporting, listener
discovery failure or synchronous callback latency. Existing business lifecycle tests
remain green, but are not substitutes for those trace assertions.

Next qualify the join's blocked-output terminal spans, then continue bounded history
aggregation and remaining source/custom/transport/durable/revision instrumentation.
Matched disabled/enabled runtime load, retained memory and a fresh coherent package
candidate remain required. Earlier measurements/packages predate these changes.
The full goal remains active. Nothing was published or sent to external consumers.
