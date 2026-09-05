# Canonical package release train

## Scope and decisions

This train contains 59 new package artifacts: 58 packages in the reverse dependency
closure of Nodes, Composition and Engine, plus the new expression adapter.
Mapping 1.0.3 and Resilience 1.0.0 are reused from the public feed, not republished.
All affected packages use a new major prerelease identity. Packages whose own APIs
are unchanged still require the breaking dependency contract; this does not claim
that every dependent package removed public members.

The core candidates are Nodes 5.0.0-rc.1, Composition 8.0.0-rc.1
and Engine 9.0.0-rc.2. Consumer rehearsal required the Engine correction described
below; its nine-package dependent closure also advances to rc.2. The expression
adapter remains a genuine first release at
1.0.0 and depends only on the reusable Mapping package. HealthChecks is not a first
release: its comparison baseline is the published 1.0.0-rc.1.

The project files and eng/packages.json remain authoritative. No package versions
are inferred from family names at execution time. Do not mix old dependent binaries
with the new core solely because a package restore can resolve their ranges.

## Reusable prerequisites

The 11 Mapping source files and six Resilience source files were compared with
mapping-v1.0.3 and resilience-v1.0.0 respectively, ignoring line-ending differences.
They are unchanged and neither package depends on the changed core. Their exact
versions were found on the public feed. Final package-only validation must use the
published prerequisite archives, not replacement archives packed under those ids.

## Package inventory

Public baselines were checked on 2026-09-05. This is an intended release set, not
an assertion that any candidate has been published. A missing candidate is checked
again immediately before its publication.

| Package | Published comparison | Candidate / reused version | Wave |
| --- | --- | --- | --- |
| FluxFlow.Nodes | 4.0.0 | 5.0.0-rc.1 | 1 |
| FluxFlow.Coordination | 2.0.0 | 3.0.0-rc.1 | 2 |
| FluxFlow.Resilience | 1.0.0 | 1.0.0 | Reuse |
| FluxFlow.Components.Resilience | 2.0.0 | 3.0.0-rc.1 | 3 |
| FluxFlow.Components.Resilience.Composition | 5.0.0-rc.1 | 6.0.0-rc.1 | 4 |
| FluxFlow.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 2 |
| FluxFlow.Mapping | 1.0.3 | 1.0.3 | Reuse |
| FluxFlow.Expressions.Jsonata | None (first release) | 1.0.0 | 1 |
| FluxFlow.Components.RequestReply | 2.0.0 | 3.0.0-rc.1 | 3 |
| FluxFlow.Components.Http.AspNetCore | 2.0.0 | 3.0.0-rc.1 | 4 |
| FluxFlow.Engine | 8.0.0-rc.1 | 9.0.0-rc.2 | 3 |
| FluxFlow.Components.Mqtt | 7.1.0 | 8.0.0-rc.1 | 3 |
| FluxFlow.Components.Mqtt.Composition | 7.1.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Mqtt.MqttNet | 3.1.0 | 4.0.0-rc.1 | 4 |
| FluxFlow.Components.Mqtt.PulseMqtt | 4.1.0 | 5.0.0-rc.1 | 4 |
| FluxFlow.Components.Mapping | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Mapping.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Assertions | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Assertions.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Sources | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Sources.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Routing | 6.0.1 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Routing.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Validation | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Validation.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.FileSystem | 6.0.1 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.FileSystem.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Observability | 7.0.0 | 8.0.0-rc.1 | 2 |
| FluxFlow.Components.Observability.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Timers | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Timers.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Payloads | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Payloads.Composition | 6.0.0-rc.1 | 7.0.0-rc.1 | 4 |
| FluxFlow.Components.Http | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Http.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Serialization | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Serialization.Composition | 6.0.0-rc.1 | 7.0.0-rc.1 | 4 |
| FluxFlow.Components.Metrics | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Metrics.Composition | 6.0.0-rc.1 | 7.0.0-rc.1 | 4 |
| FluxFlow.Components.Projections | 7.0.0 | 8.0.0-rc.1 | 2 |
| FluxFlow.Components.Projections.Composition | 6.0.0-rc.1 | 7.0.0-rc.1 | 4 |
| FluxFlow.Components.Expectations | 6.0.0 | 7.0.0-rc.1 | 3 |
| FluxFlow.Components.Expectations.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Designer | 6.0.0-rc.1 | 7.0.0-rc.1 | 3 |
| FluxFlow.Components.Sessions | 6.0.1 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.Sessions.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.State | 6.0.0 | 7.0.0-rc.1 | 2 |
| FluxFlow.Components.State.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Storage | 7.0.0 | 8.0.0-rc.1 | 2 |
| FluxFlow.Components.Storage.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 | 4 |
| FluxFlow.Components.Storage.FileSystem | 5.0.0 | 6.0.0-rc.1 | 3 |
| FluxFlow.Components.Storage.SqlFile | 5.0.0 | 6.0.0-rc.1 | 3 |
| FluxFlow.Fluent | 5.0.0-rc.1 | 6.0.0-rc.2 | 4 |
| FluxFlow.Fluent.Hosting | 5.0.0-rc.1 | 6.0.0-rc.2 | 5 |
| FluxFlow.Engine.DurableInput | 2.0.0-rc.1 | 3.0.0-rc.2 | 4 |
| FluxFlow.Engine.DurableInput.SqlFile | 2.0.0-rc.1 | 3.0.0-rc.2 | 5 |
| FluxFlow.Engine.DurableInput.TSql | 2.0.0-rc.1 | 3.0.0-rc.2 | 5 |
| FluxFlow.Engine.DurableOutput | 4.0.0-rc.1 | 5.0.0-rc.2 | 4 |
| FluxFlow.Engine.DurableOutput.SqlFile | 4.0.0-rc.1 | 5.0.0-rc.2 | 5 |
| FluxFlow.Engine.DurableOutput.TSql | 3.0.0-rc.1 | 4.0.0-rc.2 | 5 |
| FluxFlow.Engine.HealthChecks | 1.0.0-rc.1 | 2.0.0-rc.2 | 4 |

## Dependency order

Generate the five waves from the actual project references:

```powershell
./eng/package-release-plan.ps1 -AlreadyAvailable mapping,resilience
```

### Wave 1

- expressions-jsonata
- nodes

### Wave 2

- components-assertions
- components-filesystem
- components-http
- components-mapping
- components-metrics
- components-observability
- components-payloads
- components-projections
- components-routing
- components-serialization
- components-sessions
- components-sources
- components-state
- components-storage
- components-timers
- components-validation
- composition
- coordination

### Wave 3

- components-designer
- components-expectations
- components-mqtt
- components-requestreply
- components-resilience
- components-storage-filesystem
- components-storage-sqlfile
- engine

### Wave 4

- components-assertions-composition
- components-expectations-composition
- components-filesystem-composition
- components-http-aspnetcore
- components-http-composition
- components-mapping-composition
- components-metrics-composition
- components-mqtt-composition
- components-mqtt-mqttnet
- components-mqtt-pulsemqtt
- components-observability-composition
- components-payloads-composition
- components-projections-composition
- components-resilience-composition
- components-routing-composition
- components-serialization-composition
- components-sessions-composition
- components-sources-composition
- components-state-composition
- components-storage-composition
- components-timers-composition
- components-validation-composition
- engine-durable-input
- engine-durable-output
- engine-healthchecks
- fluent

### Wave 5

- engine-durable-input-sqlfile
- engine-durable-input-tsql
- engine-durable-output-sqlfile
- engine-durable-output-tsql
- fluent-hosting

Wait for each prerequisite's exact version to be publicly indexed and restorable
before publishing a dependent wave. Recheck collisions; never skip duplicates or
reuse a previously published identity for newly built bytes. Each package still
passes its own publish workflow, archive inspection and post-publication checks.

## Contract changes and consumer migration

The existing canonical-value migration and event-stream changes are accepted
breaking changes, not a reason to maintain parallel transports. Consult
[the event migration](48-canonical-event-migration.md) and each package's source
and README for component-owned input/output value shapes. Component factories,
materializers, event bindings and typed authoring helpers must be recompiled.

Binary checks compare against the latest published package listed above. Only
reviewed API targets may appear in package-local CompatibilitySuppressions.xml;
no global suppression or fake first-release baseline is allowed. An accepted
comparison does not promise compatibility for old compiled consumers.

The review found 264 distinct API targets across 45 packages, represented by 528
exact baseline-only exceptions for the two target frameworks. These cover event
and data port signatures, canonical node contracts, materializer-aware
constructors, and typed authoring handles. The other 13 affected packages have
no direct API differences against their published baselines, but still require
the coordinated dependency upgrade. The first-release expression adapter has
no published baseline; neither reused prerequisite needs an exception.

## Validation and remaining boundaries

The local rehearsal completed on 2026-09-05:

| Check | Result |
| --- | --- |
| Release metadata preflight | 61 packages passed |
| Public version availability | 59 candidates missing; both reusable versions present |
| Full Release build | 146 projects; zero errors; eight existing nullable warnings |
| Unit suite | 2,904 passed across 69 test runs; zero failures or skips |
| Release-governance rerun after API review | 202 passed |
| Deterministic acceptance suite | One passed |
| Broker-to-HTTP container integration | One net10.0 test passed; zero failures or skips; real disposable broker and loopback HTTP socket |
| Durable-input database integration | 90 net10.0 tests passed; zero failures or skips; 5 m 6 s |
| Durable-output database integration | 117 net10.0 tests passed; zero failures or skips; 6 m 41 s |
| Final binary/package validation | All 59 candidates passed with exception generation disabled |
| Candidate archive inspection | 59 package and symbol pairs passed |
| Packed internal dependencies | All 61 packages and 244 framework-specific dependency edges matched project references and candidate minimum versions |
| Isolated complete-set restore | 61 exact versions; all restored archive hashes matched the verification feed |
| Complete-set assembly/type loading | All 61 packages loaded on both net8.0 and net10.0 |
| Package-only execution acceptance | Ten-package net8.0 consumer passed workflow, code-first, resource, health, Fluent, durability and separate-process restart recovery checks |

The complete-set consumer uses exact package references, an isolated cache, no
project references, and a source mapping that restricts all FluxFlow packages to
the verification feed. That feed contains the 59 validated candidate archives and
the two actual published prerequisite archives. Assembly/type loading validates
the dependency closure, not every component's behavior or external integration.

The rehearsal artifacts are under artifacts/package-train-validated-20260905;
the complete-set consumer and dependency audit are under
artifacts/package-train-consumer-20260905. These are local verification artifacts,
not a replacement for packages built by the publish workflow from a reviewed
release commit.

Publishing, commit/tag review, remote credential verification and the actual
client application upgrade remain separate operations.
Local acceptance is not a substitute for those release gates or for validating
the client's actual workflows. Nothing was published, tagged, committed or pushed
by this rehearsal.

Follow-up integration check: the broker-to-HTTP container suite passed using
`./eng/test.ps1 -Suite Integration -Configuration Release -NoBuild`. After explicit
user authorization of the container-image license, both database-provider
runners passed sequentially with `-AcceptLicense -BlameHangTimeoutSeconds 120`.
Both used image digest
`sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
The runners removed their disposable containers and temporary result directories;
cleanup was verified afterward. These are real-provider checks against project
references, supplementing rather than replacing the package-only checks above.
No runtime or test-code changes were needed for the provider verification.

## Consumer migration handoff

The user selected FluxMQ-NG at C:/Projects/FluxmqNg, not the older FluxMQ project,
as the pre-publication consumer. Its existing task received the local feed path,
exact dependency versions, current migration guides and behavioral acceptance
checklist. The recipient has started the rehearsal; consumer acceptance is not
yet claimed.

The feed's package-index.json records all 61 package identities, archive names
and SHA-256 hashes. Keep those archives unchanged while the consumer restores
them into an isolated cache. All FluxFlow dependencies must resolve from that
local feed, without source references or fallback to old cached binaries.
Report any library defect with a minimal reproduction before preparing changed
candidate bytes. No public publication is part of this handoff.

The recipient was explicitly told that its earlier reporting notes described a
superseded intermediate design. The current event stream has no library-owned
report catalog, filter language, archive/replay or transactional reporting
snapshot. Consumer queries, calculations, persistence and widgets remain
consumer responsibilities; delivery limits must remain visible to users.

## Consumer-discovered overflow correction

The initial Engine 9.0.0-rc.1 candidate is blocked for publication: the extra
canonical-observation adapter silently retired when its own buffer filled.
The six new Engine regression cases verify overflow fault/rejection context,
continued output delivery, normal completion and idempotent disposal. Both
overflow cases failed against the original production code before the fix.

Engine 9.0.0-rc.2 returns the existing canonical observation directly. Converted
observers fault and report ObservationOverflowed when their own target fills.
No public API or compatibility exception was added. The nine dependent packages
advance to rc.2 so every declared Engine dependency path requires the correction.

The replacement feed is artifacts/package-train-observer-rc2-20260905. It has
61 indexed archives: ten new candidates and 51 byte-identical reused archives.
All 61 original frozen hashes remain unchanged. Do not overwrite either feed.
The replacement complete-set consumer is artifacts/observer-overflow-consumer;
bounded regression evidence and verification scripts are in
artifacts/observer-overflow.

Verified for the replacement: 146-project nonincremental Release build (zero
errors, eight pre-existing nullable warnings), all 165 Engine tests, solution
discovery of all six new cases, ten binary/package and symbol gates without new
exceptions, 244 internal dependency edges, 61 exact isolated-cache hashes,
61-package type loading on both frameworks, and package-only net8.0 execution
plus separate-process restart recovery. The full unit run found only a missing
candidate changelog heading in the release suite; headings were added and the
202-case release rerun passed with zero failures or skips. Earlier real-provider results above describe
the original candidate, not a rerun of unchanged provider logic for rc.2.

FluxMQ-NG received the new feed, index and exact Engine version/hash for the
unchanged strong overflow regression and broader consumer retest. Its original
candidate had passed 15 targeted real-broker cases and all 466 Core tests; Runtime
was 206 passed/one failed (overflow). UI had the identical 137-failure baseline
with 1112 passing tests, and some desktop targets lacked the required workload.
Those consumer-only limitations are not erased by a passing library build.
The replacement consumer confirmed the unchanged overflow regression passes and
the full Runtime suite is green (207/207). It verified all 61 feed hashes and
nine isolated restored graphs; its clean Web/Service/E2E build had zero warnings
or errors. The final broker/service rerun passed 18/18 with fixture cleanup
verified. Core passed 466/466. The additional UI wait-timeout failure passed an
unchanged focused repeat, then the unchanged full UI repeat returned 1112 passing
and 137 failing tests with exactly the original failure set (no new or removed
failures). No reproducible candidate UI regression remained.

FluxMQ-NG accepted Engine 9.0.0-rc.2 for the exercised package-only consumer lane.
This is not product-wide release sign-off: the existing 137 UI failures, stale
browser journey selector and missing desktop workload remain consumer limits.
The browser controller compatibility path also does not establish mapped
workflow/reporting parity. Its detailed report is
C:/Projects/FluxmqNg/docs/development/package-migration-rehearsal-2026-09-05.md;
final results and the exact UI baseline comparison are under that project's
artifacts/package-rehearsal. Both candidate feeds remain immutable. Nothing was
published, committed, tagged or pushed.
