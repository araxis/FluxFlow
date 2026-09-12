# Context-boundary local candidate

Version: `0.0.0-local.20260912.context.1`. This replaces startup.1 for current local
consumption of that checkpoint; the old feed is preserved. It predates the later
[HTTP correction](2026-09-12-http-context-boundary.md), which requires a fresh
candidate. Nothing was published or sent to another task.

[Acceptance record](../../artifacts/core-refactoring/local-candidate-20260912-context/acceptance.json)
and [repeatable final audit](../../artifacts/core-refactoring/local-candidate-20260912-context/finish.ps1).
Feed: `artifacts/core-refactoring/local-candidate-20260912-context/feed`.

| Requirement | Evidence |
| --- | --- |
| Coherent package set | 81 packages, 81 symbol archives, 81 current READMEs, 162 qualified target DLLs |
| Actual package consumption | Isolated all-package restores on net8.0/net10.0; 162 copied DLL hashes match; ten commands passed, including separate-process durable recovery |
| Small direct dependency path | Four Core/Fluent consumers; twelve commands passed; Core-only or Core+Fluent internal dependency closures verified |
| MQTT context fix from packages | Existing nine cases linked unchanged into a package-only fixture and passed on each framework; four commands passed; all six production dependency DLL copies match |
| Source qualification | Same 1,490 inputs as the clean nonincremental build and 4,857-pass local regression; 76 nonempty suites and five explicitly filtered integration/acceptance suites |
| Scope of change | Four source paths differ from startup.1: two MQTT call sites, one internal helper, one test file; no Core source or binary change |

The MQTT runtime and its four dependent package families have changed DLL hashes
on both frameworks: ten of 162 target assemblies. Other binaries are unchanged.
Internal package coordinates are consistently updated across all 81 packages;
assembly identities intentionally remain those of the qualified Release build.
The MQTT README also changed. Source snapshots and packaged README checks cover
these separately.

The nine MQTT cases cover recorded/unrecorded outbound context, absent or
hierarchical ambient context, valid/invalid/missing inbound context and a queued
publisher-to-trigger span chain with exact binary content. They are reruns of
existing cases, not 18 additional unique solution tests. The fixture links only
test/helper source and resolves production code exclusively from packages.
Its broker forwarding is simulated; real MQTT wire integration remains unrun.

The package runners and frozen results are under
[candidate artifacts](../../artifacts/core-refactoring/local-candidate-20260912-context/PLAN.md).
Historical performance/update results retain their original build identities;
unchanged Core binaries do not turn them into new MQTT measurements. Real provider
integration, remaining cross-cutting acceptance and external FluxMQ-NG validation
remain separate. This is a qualified local candidate, not complete release signoff.
