# Registration cleanup completed — step 77

The component registration cleanup is complete. Custom components, builtins,
samples and runtime construction now use the same `AddComponent(type, factory)`
path: construct a node, declare its actual ports through
`context.HasInput/HasOutput/HasEvents`, and return it. Async factories and explicit
additional lifetime ownership remain supported. There is no component contract
or activation-result wrapper to construct.

## Removed and preserved

Removed ComponentContract (all variants), ComponentContractCollection,
ComponentNodeActivation, ComponentPortBinding, the contract registration and
builder overloads, CreateComponentMap, ApplicationSetup.ComponentContracts,
and the empty AddFluxFlowComponents bootstrap. ApplicationLinkCompiler now
accepts actual constructed instances only. ContractApplicationExtensions was
renamed ApplicationConstructionExtensions. The reviewed public API baseline
records these intentional breaking removals; no forwarding compatibility layer
was introduced.

The 45 builtin registrations, 19 families, 44 typed AddX methods and 55 capture
overloads remain. Resource contracts remain because they provide a separate,
still-used resource construction capability. JSON configuration shape, typed
port handles, request/reply, processing settings, native signals, events,
owned-resource rollback and Engine revision/reload behavior remain covered.
Configuration and payload conversion stay with each component.

Direct binding now also rejects the reserved output-policy name and invalid
processing capability enum values, preserving validation previously supplied by
the removed contract path. The runtime transport was not redesigned.

## Source change accounting

Counts compare the captured source immediately before this cleanup with the
result, not the much larger inherited Git diff. Renames count as a removed and
added file. Generated code, documentation, project files and API baseline JSON
are excluded. Benchmark scope is the three captured changed files.

| C# area | Changed paths | Removed lines | Added lines | Net change |
| --- | ---: | ---: | ---: | ---: |
| Production src | 18 | 846 | 162 | -684 |
| Tests | 60 | 2,438 | 1,048 | -1,390 |
| Samples | 9 | 108 | 209 | +101 |
| Benchmarks | 3 | 16 | 39 | +23 |
| Package acceptance fixture | 2 | 26 | 50 | +24 |
| Total | 92 | 3,434 | 1,508 | -1,926 |

Four obsolete production files were deleted; one additional production file was
renamed. Production source across the affected paths shrank from 3,702 to 3,018
lines. Samples grew because their factories now explicitly declare their ports;
the framework no longer hides those declarations behind contract templates.

## Validation and test accounting

Release solution build and pack succeeded. Final configured local regression:
**4,627 passed, zero failed, zero skipped, 76 reports**, including 202 release
convention tests and the normal public API baseline check. This is 61 fewer
cases than step 76, not 61 lost runtime scenarios. Removed/replaced tests dealt
with deleted template constructors, descriptor catalogs, contract-vs-instance
mismatches and noncanonical contract declarations that the direct API cannot
express. Registration conflicts, actual port validation, ownership/failure
cleanup, event routing, compiler behavior and reload retain coverage on the
current path. Two test-only fixtures support malformed compiler surfaces and
factory enumeration; neither restores a production contract abstraction.
A logging test now subscribes to live output before sending, removing a test-only
subscription race without changing production delivery semantics.

The local suite uses the existing eng/test.ps1 exclusions: Acceptance,
ContainerIntegration, ExternalSmoke and TSql.IntegrationTests. An additional
unfiltered attempt was unsuccessful: 201 T-SQL cases required an unset
FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING, and the container integration case
failed on its environment prerequisite. These are recorded separately, not
counted as passes or silently waived. No external-service conformance is claimed.

Fresh final candidate **0.0.0-local.20260914.cleanup.2** contains **63 packages**,
including the cleaned package READMEs. Separate package-only consumers passed on
**net8.0 and net10.0**:

- All 19 family registrations expose 45 factories; typed/DI Serialization roundtrip
  succeeds. Assets contain 43 FluxFlow packages and no project references.
- Full package acceptance passes JSON and typed Engine construction, resources,
  health, direct execution and SQLFile durability, including separate-process
  restart/recovery and output idempotency. Assets contain 10 FluxFlow packages
  and no project references.

All resolved FluxFlow dependencies use the final candidate version. The net10
acceptance run uses an artifact-only retargeted fixture/helper; the maintained
acceptance helper remains net8.0. No real broker/server dependency was installed.
The expected nonpackable HttpTrigger sample warning is not a library pack failure.

Evidence: artifacts/legacy-cleanup-20260914/verification.json (package SHA-256,
resolved dependency identities and test totals), metrics.json, regression-qualified/,
regression-qualified.log, pack-final.log, families-final-net8.log,
families-final-net10.log and acceptance-final-net8/net10.log. Earlier failed
attempts remain alongside the final evidence. Focused runs are not added to totals.

## Documentation and next boundary

The current registration guide is docs/40-component-registration.md. Typed C#,
package authoring, Engine, component-family READMEs, migration guidance, samples,
benchmarks and the package-only fixture now describe/use direct factories.
Historical design/migration records are retained as history, not active API advice.
The continuation checkpoint and both implementation ledgers identify this closeout.

No builtin or custom-template migration remains inside this solution. The next
separate boundary is consumer adoption (FluxMQ-NG) and a release/version decision
for these breaking APIs. No publication, push, merge, external consumer edits or
FluxFlow.UI changes were performed.
