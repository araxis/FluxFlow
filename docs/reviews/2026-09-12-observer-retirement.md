# Consumer SDK and native observer retirement

The selected application/observer retirement check passes with a real, long-lived
consumer telemetry SDK. No library code, public API or package changed.

## Executed requirement evidence

| Requirement | Verified case and outcome |
| --- | --- |
| SDK outlives application scopes | `Long_lived_sdk_does_not_retain_retired_applications`: one SDK provider remains live through eight independent application lifetimes in each run |
| Collection continues correctly | `VerifySignals`: sixteen roots, request spans, processing spans and correlated component logs per lifetime, plus exact cumulative duration histogram count |
| Business results and ownership | `RunApplicationAsync`: sixteen exact replies with distinct exchanges, sixteen native completion events, exact node processing and once-only disposal |
| Explicit observation quiescence | Event barrier, application stop, native target unlink/complete/drain, provider disposal, SDK flush and intentional exporter-storage release all precede collection |
| Retired objects become collectible | `Retired_observers_are_collectible`: application/provider/component/native-target probes clear while SDK lives; SDK/tracer/meter/logger-factory probes clear after SDK disposal |

Four fresh qualification processes—two per framework in opposite framework order—
completed **512 requests across 32 application lifetimes**. All **128 application
probes and 16 SDK probes** became collectible. Every final checkpoint succeeded
on its first full collection attempt. The probe helper permits at most eight
attempts separated by asynchronous yields; it fails with the remaining owner names.
Two final smoke processes are separate from these totals.

Frameworks: .NET 8.0.30 and .NET 10.0.11. Consumer SDK/in-memory exporter: 1.18.0,
pinned only in the fixture. The frozen context.1 package references resolve five
library packages. All ten target DLLs match both the package archives and the
current Release build. Thus these dependency binaries are current-equivalent;
the HTTP correction is outside this fixture's dependency closure. This is not
an assertion that context.1 contains that HTTP correction.

Fresh nonincremental solution and consumer builds passed with zero warnings/errors.
All 1,491 source inputs and all captured fixture/runtime/report hashes stayed
unchanged. The existing 4,861-test regression checkpoint remains source-valid;
it was not rerun or incremented by these artifact-local cases.

Evidence: [qualification](../../artifacts/core-refactoring/observer-retirement-20260912/qualified-reviewed/verification.json),
[fixture](../../artifacts/core-refactoring/observer-retirement-20260912/Program.cs),
[runner](../../artifacts/core-refactoring/observer-retirement-20260912/verify.ps1),
[assertion/gap review](../../.testagent/observer-retirement-review.md).

## Initial checks and limits

The preserved initial attempts include two fixture corrections: disposal hook
naming and hosted-port access before startup. A subsequent .NET 10 smoke saw the
four SDK owners still referenced after disposal. Explicitly releasing the async
fixture's own strong SDK variables removed that failure; eight additional smoke
processes and the final batch passed without relaxing the probe limit. No root
dump was collected, so no specific SDK root-chain defect is claimed. No production
fix was needed.

The final assertion review added an exact four-probe requirement, preventing an
empty list from vacuously passing. Both smokes and all four runs were repeated in
`qualified-reviewed`; earlier attempts remain preserved and do not contribute to
the reported final totals.

The in-memory exporters deliberately store records. Their lists are cleared only
after flushing, to distinguish consumer-requested storage from independent roots.
This does not promise collection while a consumer intentionally holds an owner or
exported record, nor disappearance of process-static instruments.

This closes the representative **disposed application/native-observer and disposed
SDK ownership scope**. Existing raw-listener failure-isolation and metric-label
tests remain separate evidence. Timed SDK overhead, retirement of replaced
components while an application remains active, remaining optional-host stop/fault
load, external integrations and a refreshed package candidate remain open. No
network exporter or backend was installed. Nothing was published or sent to an
external consumer task.
