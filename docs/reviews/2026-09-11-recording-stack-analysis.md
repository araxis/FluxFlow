# Recording stack analysis: invalid ambient restoration

## Finding

The current Core attempts to restore an activity that can already be stopped.
The runtime rejects that assignment by creating, throwing and swallowing an
invalid-operation exception. This is observable work despite no exception escaping
the component. Both recorded workload traces contain the path
`FlowTracing.RestoreCurrent → Activity.NotifyError`, and a direct reproduction
against the frozen Core confirms the mechanism on both supported runtimes.

The next change should be small: normalize a finished saved activity to no current
activity before restoration, preserving a still-running saved activity and the
existing callback-isolation behavior. Do not remove scopes, context propagation,
reliable delivery or observer support to make this path faster. No production
change has been made in this analysis slice.

## Measured evidence

All four focused traces convert without reported loss or truncation. Converted
allocation counts, weighted bytes and type groups reconcile against a fresh raw
read; converted thread counts and sample types also reconcile against the raw
read and previous validation metadata. Every allocation and thread sample has a
nonempty stack. Unresolved native frames remain explicit; nonempty stacks do not
mean that every frame has resolved symbols.

| Recorded runtime | First application allocation frame: StartProcessingCore | First application allocation frame: RestoreCurrent | Sampled InvalidOperationException type weight | Managed thread samples including NotifyError |
|---|---:|---:|---:|---:|
| 8.0.30 | 46.34% | 11.44% | 6.43% | 1,068 / 2,674 (39.94%) |
| 10.0.11 | 58.99% | 13.03% | 7.07% | 9,880 / 40,905 (24.15%) |

The central two-to-eight-second subset retains the same leading allocation
origins: StartProcessingCore 45.87% / 59.91%, RestoreCurrent 11.10% / 13.13%.
That subset is a timestamp sensitivity check, not a proven production-only window.
Allocation weights estimate bytes between ticks, not exact object sizes or counts.
The table does not add overlapping allocation and thread percentages together.

Thread sample types remain separate. In the net8 recorded trace, 53,497 of 103,491
external-type samples also include NotifyError. External samples include waits
and other non-managed execution; they must not be discarded or reclassified as
exact CPU time. These percentages identify a hot path, not an expected speedup.

Sample totals reconcile exactly:

| Capture | Allocation samples | Thread samples | Missing stacks |
|---|---:|---:|---:|
| net8 off | 153,556 | 114,536 | 0 |
| net8 recorded | 75,189 | 106,165 | 0 |
| net10 off | 154,646 | 111,602 | 0 |
| net10 recorded | 200,381 | 56,598 | 0 |

## Direct reproduction and source check

The small artifact-local reproduction calls the public suppression scope against
the frozen Core. It captures a running ambient activity, clears it via the scope,
and either leaves it running or stops it before scope disposal. It observes
first-chance invalid-operation exceptions originating from the runtime Activity
type, without changing production code or adding a listener/exporter.

On both 8.0.30 and 10.0.11:

- Running saved activity: restored by reference; zero observed exceptions.
- Stopped saved activity: not restored; current remains null; one observed exception.

The copied Core DLL hashes match the frozen workload binaries. The reproduction
is diagnostic evidence, not a new unit-test suite or performance measurement.
The first reproduction build exposed a net8 dependency-version conflict; explicitly
referencing the frozen dependency resolved it. The final analyzer and reproduction
builds both have zero warnings/errors. Empty-feed restore required permission to
read the SDK configuration; no new feed or package was installed.

The current helper assigns the saved activity unconditionally. Runtime source
confirms that the Current setter rejects stopped activities and that NotifyError
throws and catches its exception internally. See the
[runtime Activity implementation](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs#L1185).
The .NET 10 behavior is established by the actual reproduction, not inferred only
from that older source. A queued execution context retaining an activity beyond
its lifetime is the likely workload route; the trace does not by itself enumerate
every captured activity's state at assignment.

## Next implementation and qualification

Add a focused regression for a saved activity that stops before restoration,
checking first-chance errors as well as final ambient state. Cover the live-parent
control, suppression and processing/aggregate scopes, listener modes and relevant
async boundaries on both runtimes. Preserve existing parent, span and delivery
tests. Avoid a new context manager, registry, listener or public abstraction.

Then guard the existing restoration helper, run the relevant tracing regressions
and full local qualification, and repeat matched untraced deep workflows against
the frozen baseline. Confirm the invalid-restoration path disappears and quantify
the remaining activity/context/tag costs. A pre-check is not a universal guarantee
against a host stopping an activity concurrently between check and assignment;
do not promise atomic ownership of a host-owned activity.

The 1,485 qualification inputs and 122 frozen runtime files stayed unchanged
during analysis. No production/API/harness/test change; the earlier 4,792-pass /
81-report regression and 5,002-case discovery checkpoint was not rerun. The full
goal remains incomplete: performance, broader telemetry boundaries/retention and
fresh local packages remain open. No publishing or external coordination.

Evidence: `artifacts/core-refactoring/recording-trace/analysis/verified-results/`
contains verification, input hashes, converted traces/logs and `sample-analysis.json`.
`repro/net8.0.json` and `repro/net10.0.json` contain direct observations. The
`analyze.ps1` runner preserves existing results and verifies stable inputs.
