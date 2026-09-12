# Integrated local candidate

Status: **approved local refactoring goal completed and integrated candidate
qualified**. The environment/license prerequisite is resolved. This does not
authorize publication or external consumer coordination.

## Integration-discovered correction

The real broker workflow delivered through the mapper to the HTTP socket but hung
at shutdown. An addressable source event bridge awaited full source completion;
the original business drain waited for that bridge before closing controls.
Merely moving every event output to the final drain fixed the hang but failed the
existing file-watch case: its event recorder closed before final source events.

The final correction orders source production and source control completion as
separate vertices in the existing preparation-time dependency sorter. Business
outputs depend on production; source event outputs depend on full completion;
control inputs depend on their linked writers. Event consumers therefore remain
open until their producer's event output drains. There is no new dispatcher,
subscription mechanism, lifetime coordinator or Core change.

Composition's `ComponentOutputPort.IsEvent` is readable metadata, set internally
when attaching addressable events. The public-declaration checkpoint intentionally
adds that property. Existing direct construction and ordinary graph hosting remain
unchanged. The seven qualification-input changes from `qualified.1` are that
metadata/attachment, the Engine drain plan and README, the API baseline, and two
test files. The changelog also records the unpublished correction.

## Verification

- Clean nonincremental Release solution build: zero warnings/errors, 23.31 seconds.
- Full configured local regression: **4,868 passed**, zero failed/skipped,
  76 nonempty suites and 81 reports. Five opt-in suites are explicitly excluded
  from this command, not counted as passing empty suites.
- Final real broker/HTTP workflow: **one passed**, including shutdown, 654 ms.
- Final in-process workflow acceptance: **one passed**, 444 ms, separate from the
  regression count and from the real-network case.
- Final real database conformance: durable input **90 passed** (5m 6s), durable
  output **117 passed** (6m 44s), zero failed/skipped. Together with the broker
  workflow this is **208 real-service cases**, plus one in-process acceptance.
- Focused control/lifetime regression: **24 passed**; existing file-watch case:
  **one passed**. These overlap the full regression and are not extra unique tests.
- The control theory adds four cases relative to `qualified.1`: linked/unlinked
  Events and Activity aliases. It asserts held-work ordering, message identity,
  final event delivery, completion and once-only disposal.

Source proof: [final regression](../../artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json).
Test review: [event-drain review](../../.testagent/event-drain-review.md).
The old aborted red runs, first incomplete fix, full-regression failure and locked
DLL build attempt are preserved. None is presented as successful qualification.

## Replacement packages

Qualified local version: `0.0.0-local.20260912.integrated.1`.
The feed is under `artifacts/core-refactoring/local-candidate-20260912-integrated/feed`.
All 81 package archives, 81 symbol archives, 81 packaged READMEs, 162 target
assemblies and 162 consumer assembly copies pass identity checks. All 36 fresh
package-only qualification commands pass on net8.0/net10.0: all-package workflow,
resource/health and durable restart, minimal Core/Fluent, protocol-context cases
and four stop/fault smoke processes. Restores use isolated caches with no project
references and verify local package hashes and dependency versions.

The seven changed qualification inputs match the reviewed scope. Compared with
`qualified.1`, 96 assemblies in 48 packages changed within the Composition/Engine
dependency closure; the other 66, including both Core binaries, are unchanged.
The previous 81 package and 81 symbol hashes still match. Earlier feeds remain
frozen. Evidence: [package acceptance](../../artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json).

The [service verifier](../../artifacts/core-refactoring/real-integration-20260912/service-verification.json)
checks completed reports, zero command exit codes, 1,492 stable source inputs,
218 unchanged test/runtime DLLs and 42 production dependency copies matching the
new packages. The database image digest is retained there. Both final database
containers and the broker fixture were removed after execution; unrelated
pre-existing containers were not modified.

## Scope and handoff

The user enabled the container engine and confirmed the database image license.
Database tests use disposable containers on random loopback ports; the real
workflow uses its existing broker and loopback HTTP fixture. Final integration
execution is net10.0 source-suite conformance, with copied production DLL hashes
checked against the local packages; it is not package-only or net8.0 server
conformance. The separately configured external-endpoint smoke suite is unrun.

Previous representative performance results retain their original build
identities and documented costs. They are not re-labelled as fresh timing for
this shutdown correction. No application SLO, exhaustive scenario matrix or
competitive throughput claim is added. Context-aware Add, templates and feedback
iteration remain deferred.

No publication, tag, commit, push or external consumer-task message is performed.
The next authorized consumer should use this new candidate rather than any older
feed; consumer coordination and publication still require a separate request.
