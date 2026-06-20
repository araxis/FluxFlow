# FluxFlow Memory Index

Date: 2026-05-31

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
- `report.md`: original FluxMq migration spike report supplied for review.
- `legacy-docs/`: historical pre-cleanup docs; current decisions override older
  API descriptions in this folder.
