# Error forwarding trace outcome checkpoint

FlowNode now traces failure and cancellation during forwarding of existing error
messages. The bypass branch runs inside its existing operation handlers, with
explicit rethrow to retain terminal forwarding behavior. It does not invoke the
component's value handler or attempt a replacement processing-error envelope.
Successful forwarding remains non-failing and preserves the exact message instance.
Components opting into error handling keep their existing processing/recovery contract.

No new abstraction, activity scope, public API, baseline update or dependency was
needed. The telemetry skill kept the instrumentation on the real admission operation;
the testing skill required native bounded-target, sampling and failure-path evidence.

## Requirement evidence

| Requirement | Exact test |
|---|---|
| R1: exact identity, bounded admission duration and terminal outcomes | Error_forwarding_scope_preserves_identity_admission_and_terminal_contract |
| R2: observer exceptions preserve error bypass | Listener_failures_preserve_error_bypass_identity |

The first theory covers bypass and explicit handling, four sampling configurations
and three terminal paths: 24 cases per framework. The second covers throwing sample,
start and stop callbacks: three per framework. Both frameworks were already enabled;
all 54 additions are new cases, not previously existing tests newly targeted.

Before the fix, three cases failed on each framework: output decline lacked error
status under propagation/full sampling, and owner fault lacked canceled outcome
under full sampling. The other cases already passed. The complete processing-trace
suite now passes 166 cases; three fresh-process repetitions of the new tests pass
162 further executions. Initial failed reports are retained.

Final nonincremental Release build: zero warnings/errors. Selected local regression:
4,771 passes in 81 reports with zero failures/skips. Discovery: 4,981 including all
54 additions. Qualification inputs: 1,484, unchanged during verification. Evidence is
recorded in artifacts/core-refactoring/error-forward-tracing/verification.json.
All three commands exited successfully; logs and TRX reports are preserved.
The review in .testagent/error-forward-tracing-review.md maps 43 assertion call sites
and eight selected pseudo-mutations; it does not claim coverage or a mutation score.

## Remaining gates

The local regression excludes acceptance, container, external smoke and database
integration suites as explicitly recorded. This checkpoint does not qualify every
possible custom error handler or the native transform node's separate forwarding
stage. That stage is not part of the transform invocation span and needs separate
ownership if instrumented. Remaining source/transport/durable/revision and non-message
command contexts, discovery/slow callbacks, enabled load/retention and refreshed local
package qualification are still required. Prior performance/package results do not
establish acceptance for the changed Core. The goal remains active; nothing published
or sent to external consumers.
