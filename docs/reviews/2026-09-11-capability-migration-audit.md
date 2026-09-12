# Capability migration audit

This audit advances the approved migration ledger; it is not a completion claim.
The previous checkpoint verified local candidate t1430. This pass checks package
and public component accounting and corrects the stale public coverage matrix.

## Current evidence

The current manifest and declared project references account for 81 packages,
compared with 62 captured at the start. Core has no outward project reference.
All 19 component Composition packages have zero direct or transitive Designer
project dependencies. Their optional presentation is in 19 family Designer
packages. Core/Json account for the remaining consolidation of the two retired
foundation/coordination packages; Composition remains an optional package.

Reflection over current net10.0 Release assemblies finds 56 public concrete node
types in component packages. Every one implements IFlowComponent, exposes public
construction without creator/activation/service-provider constructor parameters,
and uses canonical FlowMessage for its public Input/Output/Events block types.
No component constructor or property getter was invoked. This verifies public
metadata, not constructor side effects or the contents of custom runtime port maps.

All 60 baseline public node types map to current types. Exact names, including
generic arity, take precedence. Four old generic types then map to the same-name
canonical replacement: FlowRetryNode<T>, TimerDelayNode<T>, TimerDebounceNode<T>
and TimerThrottleNode<T>. The baseline also contained their nongeneric variants,
so this consolidation explains 60 becoming 56. No other baseline node type lacks
a current counterpart. Mere name correspondence does not prove feature parity.

The audit inventories all public node constructors and standard port metadata,
all manifest package/project references and transitive dependencies, and test
projects directly referencing each package. Existing qualified TRX reports are
associated with those test projects. These associations are navigation evidence,
not method-level coverage: a broad release suite referencing a package does not
prove every operation in that package was exercised.

## Reviewed replacements and fresh execution

Current timer classes derive from the canonical FlowNode. Delay/debounce retain
their own bounded timing stages; throttle forwards through the canonical output.
Those local timing stages implement component behavior and are not independent
application routing kernels. Retry declares Input/Ack/Nak/Cancel as actual native
business inputs and emits canonical output with attempt and lineage data.

Fresh runs, all net10.0 with zero failures/skips:

| Suite | Passed | Concrete assertions inspected |
| --- | ---: | --- |
| Timers | 77 | Direct_timer_chain_preserves_binary_and_error_identity; Direct_debounce_stop_flushes_only_latest_input; direct fault/cancellation cases |
| Retry | 35 | Direct_component_uses_native_business_ports_and_preserves_message_data; Upstream_error_is_forwarded_without_starting_an_attempt; suite retains attempt, timeout and competing-feedback cases |
| Authored workflow acceptance | 1 | Authored_mqtt_value_maps_through_jsonata_to_http_and_preserves_flow_context verifies mapping/body, exact HTTP call count, invalid shape without transport work, and lineage |

The authored acceptance case uses a supplied MQTT message and an in-process HTTP
handler. It does not connect to a broker or real HTTP socket. It is separate from
the 4,839-pass local regression filter and was freshly rerun here. Timer/retry
cases overlap that regression; do not add them to produce a new unique total.

The real container workflow suite provisions a pinned broker and real loopback
HTTP endpoint. Both ordinary and approved read-only runtime checks found no
active daemon pipe. The configured privileged check targeted
dockerDesktopLinuxEngine and returned a missing-pipe error. This is an unrun
integration prerequisite, not a passing test or proof of a protocol defect.
No daemon was started, license accepted, external endpoint used or consumer
message sent by this audit.

## Requirement disposition

| Approved requirement | Current evidence and disposition |
| --- | --- |
| Account for every current package family | All 81 rows inventoried; current references and qualified package identity linked. Per-capability behavior signoff remains open. |
| Account for original public component types | All 60 mapped; 56 current types reflected; four consolidations inspected and affected suites rerun. Constructor/options and all-operation parity remains a separate review. |
| Optional authoring/editor dependencies | Core zero outward project references; 19 ordinary composition closures have no Designer. Direct minimal package consumers already verified. |
| One runtime and exchange implementation | Prior source-backed execution-owner audit applies unchanged. Type counts/port metadata here do not substitute for that execution trace. |
| Deletions have replacements and tests | Package/type replacements accounted for; an exhaustive file-level deletion/behavior ledger is not established by this inventory. |
| Real provider/protocol behavior | In-process acceptance passed; configured container daemon unavailable. Remote/licensed provider and external smoke prerequisites remain explicit. |
| Complete performance and telemetry matrix | Existing matched measurements and selected tracing tests are partial evidence; this audit adds no performance or full export/context signoff. |
| Coherent local candidate | t1430 remains source/binary qualified and unchanged; no need to repack for this documentation-only pass. |
| Final seven-part handoff | Still incomplete: full feature/migration signoff, remaining integration/performance evidence and consolidated risks must be finished. |

## Artifacts and reproducibility

Under artifacts/core-refactoring/capability-audit-20260911:

- inventory.ps1 reads the captured baseline, current manifest, qualified source
  snapshot, test projects, TRX reports and public assembly metadata.
- packages.json contains all 81 packages, baseline planned source associations,
  current dependency closure and test/report navigation. Baseline associations
  come from the original destination plan and retained IDs, not a file-move proof.
- components.json contains the 56 public types, constructors, standard ports and
  assembly hashes. An isolated load context avoids conflicts with the shell's
  preloaded dependency versions; dependencies come from the qualified consumer.
- component-migration.json contains all 60 old-to-current type rows. Its status
  explicitly leaves signature and behavior review open rather than auto-passing it.
- inventory-summary.json records counts and structural checks.
- acceptance, timers and retry directories contain the fresh TRX reports.
- verify.ps1 and verification.json link the audit to unchanged qualified sources
  and candidate archives and validate fresh report counts.

The public docs/17-component-coverage-matrix.md now describes current package
boundaries and honest evidence levels. It no longer calls retired packages active,
claims ordinary composition depends on Designer, or suggests already-existing
live updates as a future feature.

Follow-up: `2026-09-11-component-contract-migration.md` closes constructor and
captured public-port-name accounting for all 60 mappings, plus the bounded review
of 178 original component options/contracts files. It uses the later qualified
startup.1 candidate. It does not promote metadata accounting to all-operation proof.

Next work should use these rows to close actual capability assertions and the
file-level replacement ledger, followed by the outstanding telemetry/performance
and available integration gates. Repeating a clean build or package refresh alone
cannot close those requirements. The overall goal remains active; nothing published.
