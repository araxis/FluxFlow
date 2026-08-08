# FluxFlow Memory Index

Date: 2026-08-08

This folder records the extraction work for `FluxFlow.Engine`.

- `01-current-state.md`: repository shape, source origin, and verification.
- `02-findings.md`: issues found during inspection.
- `03-removal-map.md`: what must stay in the engine and what should move out.
- `04-architecture-decisions.md`: boundary decisions for the package.
- `05-development-plan.md`: step-by-step build plan.
- `06-deploy-plan.md`: GitHub and NuGet release plan.
- `07-progress-log.md`: running history of completed work.
- `08-documentation-consolidation.md`: docs cleanup decisions.
- `09-node-authoring-helpers.md`: optional base classes and registration helpers.
- `10-runtime-review-fixes.md`: runtime review fixes for fanout, lifecycle, diagnostics, and disposal.
- `11-fluxmq-adoption-report.md`: current FluxMq adoption feasibility, migration shape, and estimated impact.
- `12-diagnostics-channel.md`: diagnostic channel decisions and runtime API.
- `13-roadmap.md`: near-term release path plus deferred DSL and component package ideas.
- `14-release-readiness-audit.md`: prerelease readiness status, gates, and next steps.
- `15-release-automation.md`: release workflow, versioning, GitHub Release, and NuGet automation.
- `16-fluxmq-migration-spike-review.md`: review of the FluxMq migration spike report and recommended sequencing.
- `17-engine-boundary-version.md`: version 0.2 engine-only boundary decision.
- `18-event-channel-rename.md`: version 0.3 neutral event channel naming decision.
- `19-conditional-links.md`: version 0.4 conditional link runtime decision.
- `20-fluxmq-migration-result.md`: first consumer migration result and remaining cleanup.
- `21-component-package-roadmap.md`: future package split for reusable component families.
- `22-package-authoring-helpers.md`: package module registration helper decision.
- `23-release-0.5.md`: version 0.5 package release record.
- `24-public-docs-rewrite.md`: focused public docs rewrite record.
- `25-validation-errors-docs.md`: structured validation and error docs record.
- `26-runtime-states-docs.md`: runtime, workflow, and host state docs record.
- `27-json-conversion-docs.md`: JSON conversion reference docs record.
- `28-expression-mapping-docs.md`: expression mapping reference docs record.
- `29-package-versioning-docs.md`: package versioning guidance docs record.
- `30-component-package-template-plan.md`: first component package template plan.
- `31-component-catalog-and-template.md`: category package catalog and reusable component template.
- `32-mqtt-component-package.md`: first MQTT component package implementation record.
- `33-independent-package-releases.md`: package-scoped release and versioning decision.
- `34-mqtt-0.2-hardening.md`: MQTT package host-integration hardening record.
- `35-mqtt-topic-validation.md`: MQTT topic validation helper and package behavior record.
- `36-mapping-component-package.md`: first generic mapping component package record.
- `37-control-component-package.md`: first generic control component package record.
- `40-component-package-template-sample.md`: buildable component package authoring template sample.
- `41-validation-component-package.md`: first generic validation component package record.
- `42-filesystem-component-package.md`: first generic file system component package record.
- `43-filesystem-read-component.md`: file read component addition and package path-policy extraction.
- `44-filesystem-watch-component.md`: file watch source component addition and lifecycle notes.
- `45-directory-enumerate-component.md`: directory enumerate source component addition and release notes.
- `46-observability-component-package.md`: first generic observability component package record.
- `47-timers-component-package.md`: first generic timer component package record.
- `48-timers-delay-schedule.md`: timer delay and cron schedule component addition.
- `49-timers-throttle.md`: timer throttle component addition.
- `50-timers-debounce.md`: timer debounce component addition.
- `51-timers-finalization.md`: first timer component set finalization.
- `52-payloads-component-package.md`: first generic payload inspection component package.
- `53-http-component-package.md`: first generic HTTP request component package.
- `54-serialization-component-package.md`: first generic serialization component package.
- `55-metrics-component-package.md`: first generic metrics aggregation component package.
- `56-sessions-component-package.md`: first generic session recording and replay component package.
- `58-state-reducer-component-package.md`: first generic state reducer component package.
- `60-component-composition-docs.md`: package composition guidance and host/package boundary notes.
- `61-package-readme-composition-links.md`: component package README links to composition guidance.
- `62-storage-component-package-plan.md`: planned generic storage component package boundary and v0.1 scope.
- `63-storage-component-package.md`: first generic logical storage component package.
- `65-storage-adapter-and-migration-plan.md`: persisted storage adapter and host migration plan.
- `66-storage-filesystem-adapter-package.md`: first file-system-backed storage adapter package.
- `67-assertions-component-package.md`: assertion package split from control and release notes.
- `68-sources-component-package.md`: deterministic source package and deferred replay boundary.
- `69-routing-component-package.md`: first routing package with switch and deferred correlation/window scope.
- `70-routing-correlation-component.md`: routing correlation node addition and release notes.
- `71-routing-switch-output-ports.md`: switch route-specific output port hardening.
- `72-routing-window-component.md`: count/time stream window component.
- `73-routing-join-component.md`: two-stream key join component.
- `74-routing-merge-fork-route-envelope.md`: merge, fork, and switch route envelope component additions.
- `75-storage-query-component.md`: storage query node and file-system adapter query support.
- `76-storage-adapter-package-rule.md`: official per-persistence-style storage adapter package rule.
- `77-storage-adapter-backend-naming.md`: concrete-backend adapter package naming refinement.
- `78-storage-filesystem-adapter-rename.md`: storage adapter rename from location-based to backend-based naming.
- `79-storage-local-package-unlist.md`: old location-based storage adapter package unlist record.
- `80-v1-readiness-plan.md`: stabilization freeze, engine v1 scope, readiness gates, and release path.
- `81-engine-public-api-inventory.md`: engine public API inventory, first cleanup, and beta-blocking API decisions.
- `82-engine-expression-adapter-split.md`: engine expression abstraction decision and concrete adapter removal.
- `83-engine-beta-release-prep.md`: engine `0.6.0-beta.1` release-prep record.
- `84-first-consumer-beta-adoption.md`: first consumer beta migration success and v1 release decision.
- `85-engine-1.0-release-prep.md`: engine `1.0.0` release-prep record.
- `86-component-engine-boundary-rebuild.md`: component package rebuild decision after the engine node identity move.
- `87-fluxmq-stable-migration-baseline.md`: first consumer stable migration result and component maturity baseline.
- `88-routing-correlation-split-inputs.md`: routing correlation split input hardening and release-prep note.
- `89-shared-expression-support.md`: shared expression support package and first Mapping migration.
- `90-control-expression-support.md`: Control migration to shared expression support.
- `91-assertions-expression-support.md`: Assertions migration to shared expression support.
- `92-state-expression-support.md`: State migration to shared expression support.
- `93-observability-expression-support.md`: Observability migration to shared expression support.
- `94-routing-expression-support.md`: Routing migration to shared expression support.
- `95-expression-support-migration-complete.md`: expression-support migration closure.
- `96-mqtt-health-forwarding.md`: MQTT adapter health forwarding.
- `97-storage-sqlfile-adapter-package.md`: single-file SQL storage adapter package.
- `98-sources-clock-hardening.md`: source clock hardening for deterministic timing.
- `99-sessions-clock-hardening.md`: session clock hardening for deterministic
  recording and replay timing.
- `268-surface-simplification.md`: central build/package ownership, authoritative
  component declarations, the Data-to-Nodes merge, canonical link projection,
  and production friend-assembly removal.
- `269-declaration-closeout-and-control-retirement.md`: release-proof declaration
  closeout and retirement of the empty Control migration markers.
- `270-designed-registration-and-immutable-catalog.md`: automatic flat designed
  registration, registration-time finalization, immutable catalog projection,
  removed public shims, 19-family migration, and verification evidence.
- `271-canonical-authoring-storage-immutability-and-hot-path-cleanup.md`:
  canonical authoring closeout, immutable storage attribute snapshots, logger
  and serializer hot-path cleanup, explicit MQTT trigger binding, and evidence.
- `272-durable-input-dead-letter-operations.md`: optional provider-neutral
  dead-letter inspection/replay, SQL-file schema v2 migration, generation CAS,
  exact verification, and the durable-output-capture recommendation.
- `273-durable-output-capture-foundation.md`: optional reflection-free capture
  of selected outputs before Engine dispatch, immutable/store contracts, flat
  registration, exact guarantees, tests, documentation, and the SQL-file next
  step.
- `274-sql-file-durable-output-provider.md`: semantic output-content comparison,
  reusable store conformance, local SQL-file provider, atomic enqueue/schema
  guarantees, provider extension boundary, verification, and delivery next step.
- `275-durable-output-delivery-foundation.md`: optional serial leased
  at-least-once output delivery, separate provider capability, lazy SQL-file
  delivery schema, exact guarantees/limits, and complete verification evidence.
- `276-durable-output-dead-letter-operations.md`: nullable bounded delivery
  attempts, atomic dead-letter settlement, bounded operator inspection,
  generation-protected replay, SQL-file delivery schema v2 migration, and
  complete verification evidence.
- `277-durable-output-provider-conformance-suite.md`: reusable capture,
  delivery, and dead-letter behavioral specifications, thin SQL-file adapters,
  provider-specific test ownership, and complete verification evidence.
- `100-filesystem-enumerate-start-diagnostic.md`: directory enumerate startup
  diagnostic race fix.
- `101-timers-clock-hardening.md`: timer clock hardening for deterministic
  timestamps and delays.
- `102-metrics-clock-hardening.md`: metrics clock hardening for deterministic
  fallback sample timestamps.
- `103-routing-clock-hardening.md`: routing clock hardening for deterministic
  route timestamps, windows, joins, correlations, and timeout delays.
- `104-observability-clock-hardening.md`: observability clock hardening for
  deterministic logger, counter, and metrics timestamps.
- `105-state-clock-hardening.md`: state clock hardening for deterministic
  reducer result timestamps.
- `106-http-clock-hardening.md`: HTTP clock hardening for deterministic
  response and error timing.
- `107-filesystem-clock-hardening.md`: file system clock hardening for
  deterministic write, read, watch, and enumerate timestamps.
- `108-validation-clock-hardening.md`: validation clock hardening for
  deterministic JSON schema validation result timestamps.
- `109-storage-clock-hardening.md`: storage clock hardening for deterministic
  logical storage and adapter timestamps.
- `110-mqtt-clock-hardening.md`: MQTT clock hardening for deterministic publish
  result and workflow event timestamps.
- `111-routing-result-timestamp-hardening.md`: routing result timestamp
  hardening for explicit package-clock-owned result times.
- `112-sessions-query-component.md`: Sessions metadata query component and
  package release record.
- `113-mqtt-reconnect-policy-hints.md`: MQTT adapter-owned reconnect policy
  hints and package release record.
- `114-projections-component-package.md`: neutral event projection package and
  release record.
- `115-expectations-component-package.md`: neutral event expectation package and
  release record.
- `116-designer-metadata-package.md`: neutral component designer metadata
  contracts and package record.
- `117-resources-component-package.md`: neutral named resource contracts and
  package record.
- `118-journal-component-package.md`: neutral event journal contracts and
  package record.
- `119-storage-query-paging.md`: storage query paging and offset hardening.
- `120-secrets-component-package.md`: neutral secret references and resolver
  contracts.
- `121-secrets-option-resolution-helpers.md`: option-facing secret reference
  helpers.
- `122-configuration-validation-package.md`: resource and secret option
  configuration validation package.
- `123-release-package-audit.md`: release package audit, helper scripts, and
  guardrails.
- `124-release-operator-note.md`: local package release dry-run and guarded tag
  command note.
- `125-release-package-list-helper.md`: read-only package alias and version
  listing helper.
- `126-release-preflight-helper.md`: read-only release preflight summary helper.
- `127-component-v1-readiness.md`: component package stable release readiness
  matrix.
- `128-component-v1-release-complete.md`: component package stable release
  completion record.
- `129-component-design-metadata-providers.md`: package-owned component design
  metadata providers for host composition.
- `130-component-design-metadata-provider-release.md`: component design metadata
  provider release plan and metadata.
- `131-full-code-review.md`: full-solution code review findings and
  remediation priorities.
- `132-review-remediation-release.md`: review remediation fixes, engine 1.1.0
  error-channel rework, component minor releases, and release plan.
- `133-expectations-deterministic-timeout-test.md`: flaky expectation timeout
  test fix with the additive observed-event-count property.
- `134-feed-verify-index-precheck.md`: flat-container index pre-check that
  makes post-publish feed verification robust to nuget.org indexing lag.
- `135-architecture-review-and-roadmap.md`: per-component review against four
  architecture principles, issue list, and the Wave 0-3 fix roadmap to 2.0.
- `136-wave2-2.0-plan.md`: review-ready Wave 2 (2.0) plan — per-node
  compile-once transformation, factory relocation, breaking-surface summary,
  and sequencing.
- `137-wave3-2.0-plan.md`: review-ready Wave 3 (2.0) plan — connection
  resource components, lazy-connect handle, TimeProvider clock consolidation,
  breaking surface, and sequencing.
- `138-2.0-ga-remediation-and-cut.md`: 2.0 pre-release review remediation
  (State clock blocker, connection dispose-race leaks, clock release guard,
  mapper diagnostic, README refresh, three flake root-cause fixes) and the GA
  cut flipping the 20 component packages from `2.0.0-preview.1` to `2.0.0`
  (engine stays `1.3.0`).
- `139-standalone-node-architecture.md`: COMPLETE re-architecture, now merged,
  tagged, and published — the `FluxFlow.Nodes` kit (`FlowNode<,>`/`FlowSource<>`,
  `AddOutput`, `OnInputCompletedAsync` drain hook, fault-flush rule,
  `FlowMessage<T>` envelope, guarded `CorrelationId`), the extracted
  `FluxFlow.Mapping` leaf, engine-free dataflow component packages (engine now
  optional), the transport-neutral `RequestReplyCoordinator` (HTTP/MQTT
  triggers), retired engine-based composition samples, and an adversarial verify
  pass that caught + fixed 3 migration regressions. Current main verifies at
  742 tests.
- `140-local-graph-maintenance.md`: local knowledge-graph output rule, hook
  support, and verification/update notes.
- `141-mqtt-connection-simplification-pilot.md`: merged and released MQTT
  interface cleanup pilot:
  node-facing `IMqttPublisher` / `IMqttTriggerSource` contracts,
  `IMqttClientHealthSource`, ack-aware `IMqttReceivedContext`, trigger
  request/reply via `MqttTriggerResponse`, publish protocol metadata under
  `MqttPublishProperties`, request-owned publish QoS/retain semantics, shared
  `CorrelatedRequestTracker` reuse for pending request/reply mechanics, removed
  connection helper/adapter/factory/profile/lease ownership, and
  next-improvement criteria.
- `142-mqttnet-adapter-package.md`: first concrete MQTT adapter package under
  `src/Mqtt`: `FluxFlow.Components.Mqtt.MqttNet`, explicit
  `MqttNetClient` session lifecycle, MQTTnet publish/trigger/health
  implementation, Last Will options, reconnect/resubscribe behavior,
  acknowledgement mapping, package manifest entry, and verification.
- `143-pulsemqtt-adapter-package.md`: second concrete MQTT adapter package under
  `src/Mqtt`: `FluxFlow.Components.Mqtt.PulseMqtt`, Pulse
  `ResilientMqttClient` lifecycle, transport injection, strict publish semantics
  with optional offline queue, route-stream trigger subscriptions, Last Will
  options, internal-managed acknowledgement boundary, package manifest entry,
  and verification.
- `144-pulsemqtt-2.0-route-subscription-release.md`: upstream Pulse MQTT v2.0
  breaking cleanup and release record: broker subscribe/unsubscribe split from
  local routing, explicit route registration/streams, PR #96 merge, stable
  `2.0.0` NuGet release, and post-release `2.1.0` preview cycle.
- `145-fluxflow-pulsemqtt-v2-adoption.md`: FluxFlow Pulse MQTT adapter moved
  from Pulse MQTT `1.1.0` to stable `2.0.0`, with the route stream API rename
  and focused verification.
- `146-pulsemqtt-onasync-convenience.md`: upstream Pulse MQTT `2.1.0` stable
  release restoring a minimal endpoint-style `OnAsync(...)` convenience over
  explicit subscribe plus local route registration, followed by the upstream
  `2.2.0` development-cycle bump; FluxFlow package dependencies were not
  changed yet.
- `147-pulsemqtt-route-template-subscribe-helper.md`: upstream Pulse MQTT
  `2.2.0` stable release adding explicit route-template `SubscribeAsync(...)`
  extension overloads so callers can subscribe parsed route templates without
  hidden string detection or repeated `ToTopicFilter`, plus the follow-up
  `2.3.0-preview.72` development-cycle publish.
- `148-pulsemqtt-litedb-storage-package.md`: upstream Pulse MQTT
  `Pulse.Mqtt.Storage.LiteDB` release record: LiteDB-backed durable
  `IMessageStore` / `ISessionStore` provider beside the existing SQLite storage
  add-on, PR #101 merge, stable `2.3.0` release with all ten packages indexed,
  and PR #102 follow-up `2.4.0-preview.75` publish.
- `149-pulsemqtt-manual-ack-adoption.md`: FluxFlow Pulse MQTT adapter adoption
  of upstream Pulse MQTT `2.4.0`; `FluxFlow.Components.Mqtt.PulseMqtt` `1.1.0`
  now maps manual trigger acknowledgement modes to Pulse acknowledged route
  streams instead of rejecting them.
- `150-mqtt-di-and-adapter-owned-features.md`: MQTT DI and adapter-owned feature
  plan implementation: adapter-local concrete client registration, hosted
  lifecycle options, optional Pulse store hooks, and release notes.
- `151-pulsemqtt-2.5-lifecycle-update.md`: FluxFlow Pulse MQTT adapter update
  to stable upstream Pulse MQTT `2.5.0`, including MQTT-named internal
  lifecycle calls and verification.
- `152-standalone-composition-layer.md`: `FluxFlow.Composition` v1
  implementation record: standalone-first composition package, explicit
  factory registration, fluent/config DTO path, direct Dataflow linking,
  lifecycle boundary, pure sample, docs cleanup, and verification.
- `153-composition-hosting-layer.md`: `FluxFlow.Composition.Hosting` v1
  implementation record: optional DI/host bridge, hosted composition runtime,
  static/config definition sources, keyed resource resolution helpers,
  diagnostics behavior, docs/manifest wiring, and verification.
- `154-mqtt-composition-adapter.md`: optional MQTT composition adapter package:
  `RegisterMqttNodes()`, `mqtt.publish` / `mqtt.trigger` factories, resource
  names for keyed `IMqttPublisher` / `IMqttTriggerSource` / `TimeProvider`,
  broker-free MQTT composition sample, package/docs/manifest wiring, and
  verification.
- `155-composition-and-designer-progress.md`: current snapshot after the
  composition adapter sweep and Designer metadata work: standalone-node-first
  architecture, completed component composition adapters, Designer boundary
  cleanup, option/resource hint pilot state, reliable verification command, and
  next suggested metadata hint pass.
- `156-assertions-designer-metadata-hints.md`: Assertions composition Designer
  metadata hint pass: option grouping/editor hints, host-owned resource key
  patterns, package `1.3.0`, focused verification, and next candidate note.
- `157-state-designer-metadata-hints.md`: State composition Designer metadata
  hint pass: state reducer option grouping/editor hints, host-owned resource key
  patterns, package `1.3.0`, focused verification, and next candidate note.
- `158-observability-designer-metadata-hints.md`: Observability composition
  Designer metadata hint pass: Counter/Logger/Metrics option grouping/editor
  hints, host-owned resource key patterns, package `1.3.0`, focused
  verification, and next candidate note.
- `159-validation-designer-metadata-hints.md`: Validation composition Designer
  metadata hint pass: JSON schema validator option grouping/editor hints,
  host-owned resource key patterns, package `1.3.0`, focused verification, and
  next candidate note.
- `160-routing-designer-metadata-hints.md`: Routing composition Designer
  metadata hint pass: Switch/Fork/Merge/Window/Correlation/Join option
  grouping/editor hints, host-owned resource key patterns, package `1.3.0`,
  focused verification, and next candidate note.
- `161-timers-designer-metadata-hints.md`: Timers composition Designer
  metadata hint pass: Interval/Schedule/Delay/Throttle/Debounce option
  grouping/editor hints, host-owned clock resource key pattern, package
  `1.5.0`, focused verification, and next candidate note.
- `162-sources-designer-metadata-hints.md`: Sources composition Designer
  metadata hint pass: Generated/Sequence option grouping/editor hints,
  host-owned clock resource key pattern, package `1.4.0`, focused
  verification, and next candidate note.
- `163-serialization-designer-metadata-hints.md`: Serialization composition
  Designer metadata hint pass: JSON/Text/Base64 option grouping/editor hints,
  host-owned clock resource key pattern, package `1.3.0`, focused
  verification, and next candidate note.
- `164-payloads-designer-metadata-hints.md`: Payloads composition Designer
  metadata hint pass: payload inspection option grouping/editor hints,
  host-owned clock resource key pattern, package `1.3.0`, focused
  verification, and next candidate note.
- `165-projections-designer-metadata-hints.md`: Projections composition
  Designer metadata hint pass: event projection option grouping/editor hints,
  host-owned clock resource key pattern, package `1.3.0`, focused
  verification, and next candidate note.
- `166-metrics-designer-metadata-hints.md`: Metrics composition Designer
  metadata hint pass: metrics aggregate option grouping/editor hints,
  host-owned clock resource key pattern, package `1.3.0`, focused
  verification, and next candidate note.
- `167-expectations-designer-metadata-hints.md`: Expectations composition
  Designer metadata hint pass: event expectation option grouping/editor hints,
  host-owned clock resource key pattern, package `1.3.0`, focused
  verification, and next candidate note.
- `168-http-designer-metadata-hints.md`: HTTP composition Designer metadata
  hint pass: HTTP client option grouping/editor hints, host-owned client and
  clock resource key patterns, package `1.3.0`, focused verification, and next
  candidate note.
- `169-filesystem-designer-metadata-hints.md`: FileSystem composition Designer
  metadata hint pass: file read/write/enumerate/watch option grouping/editor
  hints, host-owned clock resource key pattern, package `1.4.0`, focused
  verification, and next candidate note.
- `170-storage-designer-metadata-hints.md`: Storage composition Designer
  metadata hint pass: put/get/query/delete option grouping/editor hints,
  host-owned store and clock resource key patterns, package `1.4.0`, focused
  verification, and next candidate note.
- `171-sessions-designer-metadata-hints.md`: Sessions composition Designer
  metadata hint pass: recorder/replay/query option grouping/editor hints,
  host-owned store and clock resource key patterns, package `1.5.0`, focused
  verification, and next candidate note.
- `172-mqtt-designer-metadata-hints.md`: MQTT composition Designer metadata
  hint pass: publish/trigger option grouping/editor hints, host-owned
  publisher, trigger source, and clock resource key patterns, package `1.4.0`,
  focused verification, and next-planning note.
- `173-designer-metadata-hint-conventions.md`: Designer metadata hint
  convention closeout: release-test guardrails for option section/importance
  hints, contract-valued editor/syntax hints, same-node related resources, and
  host-owned resource key patterns.
- `174-designer-metadata-hint-release-readiness.md`: Designer metadata hint
  release-readiness record: broad verification, all impacted package
  preflights, Designer dry-run success, and composition dry-run dependency
  source blocker.
- `175-designer-metadata-hint-dependency-source-readiness.md`: Designer
  metadata hint dependency-source readiness record: full temp package source
  seeding, all impacted package preflights, and all impacted fast dry-run
  success.
- `176-designer-metadata-hint-publication-sequencing.md`: Designer metadata
  hint publication-sequencing handoff: dependency-aware order, release-helper
  command templates, prepare-only checks, and tag availability notes.
- `177-designer-metadata-hint-final-release-rehearsal.md`: Designer metadata
  hint final no-publish release rehearsal: fresh full temp source, all
  dependency-ordered dry-runs, prepare-only checks, and final release execution
  recommendation.
- `178-designer-metadata-hint-local-tag-execution.md`: Designer metadata hint
  local tag execution: controlled Release build, full temp source seeding, 42
  local annotated tags created at the release target, and 2 existing tags
  skipped.
- `179-designer-metadata-hint-tag-push.md`: Designer metadata hint tag push:
  42 dependency-ordered release tags pushed to the configured remote, 2
  existing tags skipped, and remote targets verified.
- `180-designer-metadata-hint-release-workflow-recovery.md`: Designer metadata
  hint release workflow recovery: Linux release-test path normalization fix,
  42 dependency-ordered tags retargeted to the fixed commit, release workflows
  completed, and public package-feed visibility verified.
- `181-mqtt-adapter-package-release.md`: MQTT adapter package release:
  `FluxFlow.Components.Mqtt.MqttNet` `1.1.7` and
  `FluxFlow.Components.Mqtt.PulseMqtt` `2.0.7` tagged, published, and verified
  on the public package feed.
- `182-public-package-consumer-validation.md`: public package consumer
  validation for the published Designer metadata hint release train and MQTT
  adapter releases: all 44 package-feed checks plus a combined temp consumer
  restore/build passed.
- `183-package-readme-clarity-pass.md`: documentation-only package README
  clarity pass across all 55 manifest packages, with boundary wording tightened
  for runtime, composition, adapter, and support packages.
- `184-package-binary-compat-readiness.md`: package binary compatibility
  readiness helper and docs, with one successful published-baseline validation
  and an all-package loop blocker on an unpublished current support-package
  version.
- `185-package-binary-compat-baseline-feed-alignment-blocker.md`: attempted
  baseline feed-alignment release pass, stopped by a Linux release-test fixture
  newline blocker before package publication.
- `186-package-binary-compat-feed-alignment-recovery.md`: release-test fixture
  newline recovery, nine missing baseline package publications, and all-package
  binary compatibility preflight success.
- `187-full-public-package-consumer-validation.md`: full public package consumer
  validation for all 55 current manifest packages, including package-feed
  checks and a combined temp consumer restore/build.
- `188-designer-resource-picker-hint-contracts.md`: Designer resource picker
  hint contracts: additive `2.17.0` host helper APIs for reading host-owned
  resource picker hints from component metadata without owning resources or UI.
- `189-designer-resource-picker-hint-package-release.md`: Designer resource
  picker hint package release: `FluxFlow.Components.Designer` `2.17.0` tagged,
  published, release-asset verified, and public-feed verified.
- `190-full-public-package-consumer-validation-after-designer-2-17.md`: full
  public package consumer validation after Designer `2.17.0`: all 55 package
  feed checks and combined temp consumer restore/build passed.
- `191-designer-host-layer-planning.md`: documentation-only Designer host
  layer plan covering host-owned palette, inspector, option editor, resource
  picker, validation, persistence, and runtime-mapping responsibilities.
- `192-composition-resource-helper-relocation.md`: keyed resource helper
  relocation onto the composition factory context, adapter
  Composition.Hosting reference removal, and the node kit clock option.
- `193-designer-host-model-layer.md`: headless Designer host-model layer
  (palette, inspector, option editor, and resource picker view models over the
  metadata catalog) in `samples/FluxFlow.DesignerHost` with focused tests.
- `194-designer-host-persistence-mapping.md`: host graph model with lossless
  mapping to and from composition definitions plus shared validation message
  mapping; renderer UI is the only remaining Designer host pass.
- `195-nuget-icon-and-hygiene-release-prep.md`: shared Fanout NuGet icon wired
  repo-wide via Directory.Build.targets; the composition hygiene pass
  (FluxFlow.Nodes 1.2.0, FluxFlow.Composition 1.1.0,
  FluxFlow.Composition.Hosting 1.1.0, and 19 components-*-composition
  adapters) published and verified live on the public NuGet feed.
- `196-full-icon-rollout-completion.md`: icon-only patch release for the
  remaining 33 manifest packages (Designer, core component packages, Mapping,
  Engine, MQTT adapters) so all 55 current packages carry the shared icon; a
  second known-flaky test surfaced and was worked around by retry.
- `197-bounded-source-flaky-test-fix.md`: deterministic fix for that second
  flaky test — the bounded-source backpressure test was asserting an
  un-observable BroadcastBlock internal race; rewritten to the actual
  latest-wins delivery contract (test-only, no source/package change).
- `198-designer-renderer-app-first-slice.md`: Designer host layer phase 5
  started — `samples/FluxFlow.DesignerApp` (Blazor WASM + MudBlazor) renders the
  palette and option/resource inspector from the real metadata catalog;
  browser-verified. Canvas and persistence are follow-on slices.
- `199-designer-renderer-canvas-slice.md`: Z.Blazor.Diagrams node canvas in the
  renderer app — add-from-palette, node rendering with ports, and
  canvas-selection-drives-inspector; browser-verified. Persistence remains.
- `200-designer-renderer-persistence-slice.md`: save/load the canvas graph as a
  FluxFlow.Composition definition (named-port link mapping) with validation
  feedback — completes the docs/18 phase 5 renderer; browser-verified.
- `201-designer-renderer-option-editing.md`: inspector option editors write into
  the selected node's configuration so saved composition JSON carries real
  option values; value round-trip browser-verified.
- `202-designer-renderer-editor-polish.md`: Designer canvas delete-selected
  (node/link, toolbar + Del key) and link-creation validation (reject self-links
  and non-output→input, with snackbar); browser-verified. Renderer merged to
  main via PR #55.
- `203-fluent-dsl-plan.md`: plan for `FluxFlow.Fluent` — a type-safe, code-first
  DSL (`Flow.From(...).Then(...).To(...)`) over the standalone nodes, reusing
  `CompositionRuntime` via a new additive public seam
  `CompositionRuntime.Create(nodes, links, entryNodes)` (Composition 1.2.0).
  Foundation built on `work/fluent-dsl`: linear `From/Then/To/Build` + `Tap`
  fan-out, `FlowGraph` lifecycle, 7 passing tests (30× flake-checked). Branching,
  fan-in, DI/hosting, sample, and package release wiring are the next slices.
- `204-runtime-and-component-review-fixes.md`: eight reliability fixes across
  Composition, Engine, Nodes, FileSystem, Timers, Routing, and HTTP, with seven
  patch-version bumps and complete package-readiness verification.
- `205-vnext-data-foundation.md`: first vNext foundation milestone covering
  FlowValue, FlowContent/codecs, FlowMessage identity, result conventions,
  package boundaries, and verification before the API-review gate.
- `206-vnext-data-foundation-api-review.md`: requirement-by-requirement public
  API review, negative-path hardening, verification evidence, and acceptance of
  the foundation before canonical Composition work.
- `207-vnext-composition-definition-addressing.md`: canonical flat Composition
  application definitions, strict deterministic JSON/config loading, shared
  ordinal addressing, versioning, verification, and the link-compiler handoff.
- `208-vnext-composition-link-compilation.md`: canonical input/output-side link
  parsing, absolute normalization, compile-once conditions, exact metadata/type
  validation, duplicate/exclusive/cycle diagnostics, and the stable-port
  runtime handoff.
- `209-vnext-stable-port-runtime.md`: Engine-owned bounded stable input
  mailboxes, output broadcast hubs, revision-safe attachment, compiled-link
  activation, direct port APIs, package verification, and the system-stream
  handoff.
- `210-vnext-system-events-diagnostics.md`: canonical bounded system-event and
  best-effort diagnostic outputs, isolated component failures, runtime/port
  status snapshots, standard .NET instrumentation, and the DI-snapshot handoff.
- `211-vnext-di-resource-provider-snapshots.md`: immutable Microsoft DI provider
  snapshots, canonical keyed resource/component/port/signal registration,
  explicit ownership boundaries, and the transactional-revision handoff.
- `212-vnext-transactional-revisions.md`: complete-definition revision
  planning, atomic stable-port activation, Engine-independent candidate
  coordination, rollback/drain semantics, and the MQTT vertical-slice handoff.
- `213-vnext-mqtt-core.md`: provider-neutral MQTT client configuration,
  controller, command/result contracts, transport SPI, nodes, subscriptions,
  acknowledgements, reconnect semantics, and the concrete-adapter handoff.
- `214-vnext-mqtt-adapters.md`: concrete implementations of the MQTT transport
  SPI, shared adapter conformance, coordinated broker acknowledgements, and the
  canonical MQTT Composition handoff.
- `215-vnext-mqtt-composition.md`: canonical nested MQTT resources, four vNext
  Composition nodes, signal-port metadata/runtime integration, package and
  consumer verification, and the component-family migration handoff.
- `216-vnext-mapping-flowvalue.md`: canonical `FlowValue` mapping with one
  normal `FlowResult<FlowValue>` output, preserved typed compatibility,
  Composition/Designer migration, and package/consumer verification.
- `217-vnext-payloads-flowcontent.md`: canonical `FlowContent` inspection with
  cached `FlowValue` reuse, normal result failures, preserved request-based
  compatibility, and Composition/package verification.
- `218-vnext-serialization-flowcontent-flowvalue.md`: canonical explicit
  JSON/Text/Base64 conversions between `FlowContent` and `FlowValue`, normal
  result failures, preserved request-based compatibility, and package evidence.
- `219-vnext-validation-flowvalue.md`: canonical `FlowValue` JSON Schema
  validation with valid/invalid normal results, preserved generic compatibility,
  explicit typed-result boundaries, and Composition/package evidence.
- `220-vnext-assertions-flowvalue.md`: canonical `FlowValue` assertions with
  pass/fail normal results, normal evaluation errors, preserved generic branch
  compatibility, and Composition/package evidence.
- `221-vnext-expectations-flowresult.md`: canonical projection-event
  expectations with matched/unmet/timeout/completion normal results, normal
  evaluation errors, exact-once lifecycle behavior, preserved standalone
  compatibility, and Composition/package evidence.
- `222-vnext-routing-flowvalue.md`: canonical FlowValue/result Window,
  Correlation, and Join nodes, deprecated structural routing, preserved generic
  compatibility, and Composition/package evidence.
- `223-vnext-control-link-deprecation.md`: canonical link-condition replacement
  for Filter and When, preserved obsolete runtime/Composition compatibility,
  Designer migration metadata, and package evidence.
- `224-vnext-state-flowvalue.md`: canonical FlowValue State commands and normal
  result outcomes, preserved object-based runtime compatibility, natural JSON
  composition binding, and package evidence.
- `225-vnext-projections-flowresult.md`: canonical typed-event projection
  snapshots and expected failures on one normal result output, ordered final
  completion, preserved direct-result compatibility, and package evidence.
- `226-vnext-metrics-flowresult.md`: canonical typed-sample metric snapshots,
  partial group-limit and expected failure results, ordered final completion,
  preserved direct-result compatibility, and package evidence.
- `227-vnext-observability-flowvalue.md`: canonical FlowValue Counter, Logger,
  and Metrics normal-result contracts, FlowValue-native selectors, preserved
  generic compatibility, and package evidence.
- `228-vnext-sources-flowvalue.md`: canonical FlowValue generated and sequence
  sources, one-or-many ordinary JSON item binding, preserved typed
  compatibility, and package evidence.
- `229-vnext-timers-flowvalue-results.md`: canonical FlowValue Interval and
  Schedule sources, FlowResult Delay/Throttle/Debounce transforms, preserved
  typed compatibility, exact-once temporal completion, and package evidence.
- `230-vnext-http-flowcontent-results.md`: canonical exact-content HTTP
  requests, polymorphic response/error results, preserved direct-use and
  Composition compatibility, and package evidence.
- `231-vnext-filesystem-flowcontent-results.md`: canonical exact-content file
  reads/writes, FlowValue directory/watch sources, preserved typed
  compatibility, and package evidence.
- `232-vnext-storage-flowcontent-results.md`: canonical exact-content storage
  records, normal typed operation results, preserved store/adapter ownership
  and typed compatibility, and package evidence.
- `233-vnext-sessions-flowcontent-results.md`: canonical exact-content session
  recording/replay, one-output query results, preserved store ownership and
  typed compatibility, and package evidence.
- `234-vnext-resource-address-ownership.md`: canonical nested resource and
  secret addresses, explicit host/revision/external ownership, non-owning DI
  bridges, Configuration alignment, and package evidence.
- `235-vnext-canonical-application-hosting.md`: canonical application sources,
  degraded hosted revision lifecycle, explicit candidate DI, preserved legacy
  Composition hosting, and package evidence.
- `236-vnext-designer-canonical-persistence.md`: canonical flat Designer
  persistence, nested resource/reference projection, declaration-side-aware
  links, signal rendering, runtime diagnostics, and package evidence.
- `237-vnext-coordinated-package-validation.md`: complete 58-package local
  source, package-origin verification, and warnings-as-errors combined consumer
  closeout evidence.
- `238-canonical-application-runtime-assembly.md`: explicit canonical JSON to
  resource/component/link runtime assembly, stable direct ports, transactional
  replacement, ownership, and verification evidence.
- `239-application-runtime-port-generations.md`: dynamic current-port
  generations for component add/remove/type changes, drain-safe ownership, and
  verification evidence.
- `240-canonical-component-type-names.md`: harmonized component operation and
  MQTT retry-resource type names, explicit input aliases, canonical-only
  Designer enumeration, package versions, and verification evidence.
- `241-canonical-composition-simplification.md`: canonical alias normalization,
  direct component factory contexts, addressable traced Events, semantic
  processing profiles, canonical Designer projection, obsolete legacy model
  guidance, versions, and verification evidence.
- `242-canonical-vnext-local-main-integration.md`: linear ancestry proof,
  fast-forward integration of the complete canonical vNext stack into local
  `main`, post-integration verification, and the retained release boundary.
- `243-filesystem-canonical-consolidation.md`: concise exact-content FileSystem
  transforms, direct FlowValue sources, removed typed compatibility surfaces,
  major package versions, and package/compatibility evidence.
- `244-storage-canonical-consolidation.md`: concise exact-content Storage
  operations, preserved store-adapter boundary, removed typed component
  compatibility, major package versions, and package/compatibility evidence.
- `245-mapping-canonical-consolidation.md`: single FlowValue Mapping contract,
  removed generic CLR compatibility, major package versions, and
  package/compatibility evidence.
- `246-validation-canonical-consolidation.md`: single FlowValue Validation
  contract, removed generic CLR compatibility and selector alias, major package
  versions, and package/compatibility evidence.
- `247-assertions-canonical-consolidation.md`: single FlowValue Assertions
  contract, removed generic CLR compatibility and duplicate engine option,
  major package versions, and package/compatibility evidence.
- `248-state-canonical-consolidation.md`: single FlowValue State reducer
  contract, removed object compatibility and numeric errors, major package
  versions, and package/compatibility evidence.
- `249-expectations-canonical-consolidation.md`: single projection-event
  Expectations contract, exact-once timeout/completion publication, removed
  direct-result compatibility, and package/compatibility evidence.
- `250-sessions-canonical-consolidation.md`: concise exact-content Sessions
  nodes, retained store-adapter boundary, removed typed node/branch/error
  compatibility, major package versions, and package/compatibility evidence.
- `251-timers-canonical-consolidation.md`: concise FlowValue/result Timers
  nodes, preserved temporal and lifecycle behavior, removed typed
  compatibility, major package versions, and package/compatibility evidence.
- `252-sources-canonical-consolidation.md`: concise FlowValue Sources nodes,
  preserved source lifecycle and deterministic timing, removed typed
  compatibility, major package versions, and package/compatibility evidence.
- `253-serialization-canonical-consolidation.md`: concise FlowContent/FlowValue
  Serialization nodes, removed request/result and temporary node compatibility,
  runtime major version, and package/compatibility evidence.
- `254-payloads-canonical-consolidation.md`: concise FlowContent Payloads
  inspection, removed request DTO and temporary node compatibility, runtime
  major version, and package/compatibility evidence.
- `255-observability-canonical-consolidation.md`: concise FlowValue
  Observability nodes, removed generic/direct-result compatibility, major
  package versions, and package/compatibility evidence.
- `256-composition-canonical-runtime-removal.md`: explicit legacy-definition
  migration boundary, removed Composition/Hosting runtime compatibility,
  canonical fan-in and cleanup evidence, major versions, and full package-set
  consumer verification.
- `257-engine-canonical-runtime-simplification.md`: decomposed canonical Engine
  preparation, stable-port planning/binding and generation ownership, shared
  input revision lifetime, isolated event publication, and package evidence.
- `258-structural-control-routing-removal.md`: canonical conditional-link,
  fan-out, and fan-in parity; removed Control Filter/When and Routing
  Switch/Fork/Merge compatibility; migration-only packages; major versions;
  and package evidence.
- `259-mqtt-canonical-consolidation.md`: one canonical MQTT controller and
  transport path, removed publisher/trigger/health and adapter compatibility,
  canonical Composition split, major versions, and package evidence.
- `260-routing-canonical-consolidation.md`: one canonical FlowValue/result
  Routing algorithm path, internalized mature algorithms, removed generic
  components/registrations/port constants, major-version evidence, and full
  package consumer verification.
- `261-canonical-vnext-cleanup-completion.md`: requirement-by-requirement
  canonical cleanup audit, reviewed removals and retained exceptions, final
  verification evidence, package readiness, and deferred boundaries.
- `262-coordination-and-resilience-refactoring.md`: port-aware signal feedback,
  generic TraceId coordination, transport-neutral resilience, RequestReply and
  MQTT migrations, canonical flow.retry, race evidence, and package readiness.
- `263-typed-flow-data-contract-simplification.md`: typed value-or-error
  messages, exact raw content, removed universal values/results/codecs, full
  component migration, benchmark evidence, major package closure, and release
  readiness.
- `264-framework-simplification-round-2.md`: consolidated node execution,
  deterministic FlowContent persistence, shared Designer metadata factories,
  centralized processing profiles, stateful runtime decomposition, package
  versions, and full verification evidence.
- `265-di-first-application-component-simplification.md`: DI-only component and
  application registration, immutable descriptors/catalog, removed registry and
  contributor frameworks, 19 adapter migrations, major package versions, and
  complete test/build/package evidence.
- `266-hosted-engine-simplification.md`: one Engine-owned application facade and
  revision lifecycle, Composition-owned extension contracts, obsolete
  Hosting-only forwarding, package boundaries, and complete verification.
- `267-major-surface-reset.md`: removal of hosting compatibility, legacy
  migrators, aliases, registry helpers, and disconnected support packages; exact
  canonical boundaries, major versions, and release evidence.
- `268-surface-simplification.md`: declaration, package-boundary, link-ownership,
  and version simplification evidence.
- `269-declaration-closeout-and-control-retirement.md`: final declaration
  closeout and retired Control migration markers.
- `270-designed-registration-and-immutable-catalog.md`: flat designed-component
  registration and immutable catalog ownership.
- `271-canonical-authoring-storage-immutability-and-hot-path-cleanup.md`:
  canonical authoring removal, immutable storage attributes, and hot-path
  cleanup evidence.
- `272-durable-input-dead-letter-operations.md`: optional durable-input
  dead-letter inspection and generation-protected replay.
- `273-durable-output-capture-foundation.md`: provider-neutral optional output
  capture contracts and Engine seam.
- `274-sql-file-durable-output-provider.md`: local SQL-file capture provider and
  idempotent no-overwrite semantics.
- `275-durable-output-delivery-foundation.md`: leased at-least-once output
  delivery contracts, dispatcher, and SQL-file state.
- `276-durable-output-dead-letter-operations.md`: bounded attempts,
  dead-letter settlement, inspection, and replay.
- `277-durable-output-provider-conformance-suite.md`: reusable capture,
  delivery, and dead-letter provider behavioral floor.
- `278-networked-relational-durable-output-feasibility.md`: successful
  direct-SQL real-server feasibility spike, 65-test evidence, isolation,
  limitations, and production-promotion boundary.
- `279-production-tsql-durable-output-provider.md`: supported opt-in direct-SQL
  network provider, flat immutable registration, explicit schema governance,
  73-test real-server evidence, packaging, and operational boundary.
- `280-durable-input-workflow-completion-acknowledgement.md`: explicit opt-in
  workflow-completion acknowledgement, exact lease renewal, provider-neutral
  contracts, SQL-file support, honest at-least-once semantics, and validation
  evidence.
- `281-production-tsql-durable-input-provider.md`: supported opt-in networked
  durable-input provider, shared atomic leasing, exact renewal, explicit schema
  governance, packaging, and real-server validation evidence.
- `282-durability-operational-status.md`: immutable payload-free input/output
  status contracts, read-only SQL-file/T-SQL inspection, exact singleton
  aliases, and validation evidence.
- `283-durable-terminal-retention.md`: explicit bounded input/output terminal
  deletion, direct transactional provider SQL, exact singleton aliases,
  destructive/idempotency semantics, and validation evidence.
- `284-durable-output-lease-renewal.md`: immutable exact-token output renewal,
  flat heartbeat settings, serial dispatcher ownership rules, direct SQL-file/
  T-SQL transitions, and validation evidence.
- `285-release-test-determinism.md`: causal fake-time retry synchronization,
  bounded test-owned process execution, prebuilt sample smoke tests, and
  complete release-gate evidence.
- `286-durability-instrumentation.md`: package-local BCL activities, bounded
  transition counters and duration histograms, listener isolation, status-query
  separation, exact signal names, and verification evidence.
- `287-durability-operations-sample.md`: runnable local durable cycle,
  host-owned BCL listeners, explicit before/after status snapshots,
  deterministic operations output, and sample-only ownership boundaries.
- `288-release-verification-and-sample-cleanup.md`: targeted child-process test
  serialization, deterministic fixture ownership, one-signal sample telemetry,
  focused boundary assertions, and repeated parallel/serialized evidence.
- `289-repository-release-readiness.md`: accumulated-work audit and commit
  organization, release-only real-provider gates, clean detached-worktree
  verification, container cleanup, and final readiness evidence.
- `290-pr-65-final-review.md`: complete pull-request review, bounded lifecycle
  and durable-store ownership corrections, static/performance/package audit,
  local and remote verification, and ready-for-review boundary.
- `291-pr-65-merge-and-post-merge-validation.md`: exact reviewed-head merge,
  causal test stabilization, package-cache isolation, real-provider proof,
  complete 59-package rehearsal, cleanup, and no-publication boundary.
- `292-coordinated-release-train.md`: fail-closed dependency-wave publication,
  58 immutable new releases from one commit, one reused prerequisite, isolated
  recovery, 59 public consumers, executable samples, and cleanup evidence.
- `293-concurrency-reliability-hardening.md`: release-failure root causes,
  skip-locked T-SQL lease-contract proof, causal receiver registration,
  mutation evidence, verification, and the no-publication decision.
- `294-binary-compatibility-release-gate.md`: explicit per-package binary
  baselines, fail-closed manifest resolution, single-path compatibility-aware
  release packaging, validation evidence, and the no-publication boundary.
- `295-package-consumer-acceptance-gate.md`: isolated package-only Engine,
  Fluent, and SQL-file durability execution, exact candidate-byte verification,
  automatic CI/rehearsal integration, and the no-runtime-change boundary.
- `296-package-consumer-restart-durability-acceptance.md`: deterministic
  package-only seed/recovery processes proving SQL-file lease recovery,
  hosted workflow/output completion, and host-owned idempotent effects without
  an exactly-once claim.
- `297-typed-component-port-binding.md`: one typed authoritative port
  declaration, explicit named component-event outputs, lifecycle ownership,
  complete family/sample migration, and intentional public API simplification.
- `298-declarative-component-port-naming.md`: clean `HasInput`/`HasOutput`/
  `HasSignalInput`/`HasEvents` terminology for mapping existing node members to
  the component contract without implying duplicate port creation.
- `299-typed-code-first-application-authoring.md`: typed component contracts,
  named handles and Events, flat workflow capture, local/cross-workflow
  connections, definition-owned C# predicates, independent JSON/C# sources,
  family/sample migration, and verification evidence.
- `300-unified-code-first-component-contracts.md`: complete component contracts
  as the single runtime/authoring authority, definition-owned descriptors,
  effective-catalog and revision rules, JSON separation, official-family and
  sample migration, and verification evidence.
- `301-end-to-end-code-first-simplification.md`: typed handles through runtime
  and durability, executable code-first resource contracts, MQTT registrar
  consolidation, canonical-backed Fluent lifecycle, advanced dynamic
  registration, and preserved JSON/ownership boundaries.
- `302-application-health-readiness.md`: optional standard .NET readiness over
  existing Engine lifecycle state, exact healthy/degraded/unhealthy semantics,
  bounded privacy-safe data, package-only proof, and zero-worker ownership.
- `303-performance-concurrency-lifetime-baseline.md`: permanent benchmark
  coverage, bounded hot-path audit, measured allocation reductions,
  deterministic concurrency/revision/shutdown proof, and manual-only timing
  policy.
- `304-release-candidate-consolidation.md`: frozen typed C# and portable JSON
  paths, package-only rollback proof, clean committed-snapshot validation,
  real SQL-provider evidence, and the no-publication boundary.
- `report.md`: original FluxMq migration spike report supplied for review.
- `legacy-docs/`: historical pre-cleanup docs; current decisions override older
  API descriptions in this folder.
