# Authoring, evaluator, editor persistence and health qualification

This closes the reviewed authoring/configuration, editor persistence and readiness
items in the behavior ledger. It does not claim all component-family behavior,
real transport integrations or performance acceptance. No production or test C#
was changed; the current candidate remains `0.0.0-local.20260911.startup.1`.

## Current execution and qualification

| Suite | Passed |
| --- | ---: |
| Json | 112 |
| Fluent | 39 |
| Fluent.Hosting | 5 |
| Composition | 271 |
| CSharp expression adapter | 7 |
| Jsonata expression adapter | 19 |
| Designer | 181 |
| Engine.HealthChecks | 32 |
| Total | 666 |

All eight suites ran on net10.0 with zero failures/skips. They use the existing
xUnit/VSTest runner and previously clean Release outputs, with `--no-build
--no-restore`; this is not another fresh solution build. Every test process exited
zero and each report contained a nonempty, all-passed result set.

The verifier checked all 1,488 source inputs against the current candidate before
and after execution. All 32 loaded FluxFlow dependency copies across the test
outputs match the candidate's target assembly hashes. Test runtime DLLs remained
unchanged during each run. The existing package-only net8/net10 qualification is
separate; these net10 test projects were not silently retargeted.

Exact commands, hashed TRX files, every executed test name, loaded dependency
hashes and source snapshot are under
`artifacts/core-refactoring/authoring-behavior-20260911`. Its `verify.ps1` preserves
completed evidence. These 666 executions overlap the 4,848-pass local regression
and do not increase its unique test count.

## Assertion-level reconciliation

Test names below identify inspected assertions, not conclusions inferred solely
from class names or green suite totals.

| Requirement | Executed evidence | What the assertions establish |
| --- | --- | --- |
| Direct/document/portable-fluent kernel parity | `DocumentKernelParityTests` direct, JSON, portable-fluent and exported-document scenarios | Actual FlowApplication execution; response 198; exact output sequences 198/4/8; one processing error; same envelope/value/header references at both fanout sinks; zero pending requests; source starts once after construction; borrowed dependency retained |
| Construction and failure cleanup | `Unknown_document_type_is_rejected_before_any_constructor_runs`, `Invalid_document_port_disposes_constructed_components_without_starting_sources`, native invalid-port/cycle cases | Unknown type causes zero constructors; later topology failures dispose constructed owned components without starting the source or disposing the borrowed dependency |
| Nonportable predicates are not lost | `Native_fluent_predicates_cannot_be_silently_exported`, `Native_predicates_execute_with_portable_conditions_and_preserve_exact_delivery` | Delegate export throws; native and textual predicates execute together when building directly |
| Link direction and fallback | `Fallback_delivery_is_exclusive_across_document_native_and_contract_graphs` | Four construction modes; positive messages only to ordinary target, other messages only to fallback; original references, exact counts, successful drain and once-only disposal |
| Predicate faults and explicit isolation | `Delivery_preserves_condition_failures_and_handler_isolation` | Exact original exception/message/addresses; isolated rejection allows healthy delivery even if reporter throws; nonisolated failure prevents sibling dispatch; no target or duplicate notification |
| Explicit terminal/reply policies | `Explicit_discard_and_reply_policies_execute_in_both_graphs`, `Invalid_output_policies_are_located_and_do_not_activate_components` | Both direct document and contract paths; independent replies on one original message; pending registry empties; duplicate reply does not reach fallback; invalid policy diagnostics and cleanup |
| Direct fluent unmatched behavior | `Unlinked_emitting_output_faults_with_its_address`, `Explicit_discard_does_not_hide_another_unlinked_output`, `Explicit_discard_preserves_subsequent_links` | Missing outputs fault visibly; opt-in discard is local and does not disable existing delivery. This supersedes the earlier execution-owner report's implicit-discard finding |
| Direct fluent observations | `Error_messages_remain_observable_workflow_data_on_the_canonical_graph`, `Throwing_event_observer_is_isolated_from_canonical_workflow_delivery` | Error code/message/category/transience/details remain data; observer failure does not prevent exact business output |
| Optional resource boundary | `Resource_documents_require_composition_before_any_binding_runs` | Minimal bindings reject resource documents before any binding callback and identify the optional construction path; no silent omission of resources |
| Workflow engine selection | `Source_workflow_selects_engine_for_both_declaration_sides`, `Missing_named_engine_does_not_fall_back_and_when_needs_no_engine` | Source workflow selects the engine for either link direction; a missing named engine is not silently replaced; native predicates require no evaluator |
| Parameter ownership and compile cache | `Parameters_are_isolated_and_cached_by_configuration`, `Parameter_projection_detaches_values_from_caller_owned_documents_and_dictionaries` | Three links/two parameter sets compile twice; threshold boundaries differ correctly; invocation dictionaries are not modified; detached structured parameters survive document disposal and dictionary mutation |
| Native expression configuration | `Host_configuration_supports_functions_constants_and_referenced_types`, `Native_parser_options_are_applied`, `Compiled_conditions_keep_concurrent_values_and_engine_instances_isolated` | Host functions/constants/types/options survive; configuration runs once; 64 concurrent evaluations per pair of engines retain their separate values |
| Structured evaluator configuration | `Native_configuration_uses_fresh_environments_and_invocation_local_bindings`, `Root_and_named_binding_use_one_serialization_per_invocation`, `Native_binding_mutation_cannot_change_root_siblings_or_later_invocations` | Fresh native environments; host functions and invocation override; one serialization; independent mutable bindings; exact original/root/sibling values after deliberate native mutation |
| Invalid evaluator configuration | `Compilation_rejects_invalid_conditions_and_assignments`, `Invalid_expression_configuration_is_rejected`, `Duplicate_parameters_and_parameters_without_a_condition_are_rejected` | Unknown identifiers, wrong result type, default-disabled assignments, invalid names/shapes, duplicate parameters and parameters without a condition fail; valid compilation still works afterward |
| Editor policy and direction retention | `Editable_output_policies_preserve_explicit_terminal_and_reply_intent`, `Editable_links_preserve_delivery_conditions_and_direction`, `Editable_fallback_links_preserve_policy_and_direction` | Load/save/reload retains explicit policies, conditional parameters, both declaration directions and all existing delivery policies; fallback cannot also have a condition |
| Lossless editor persistence | `Malformed_link_property_remains_raw_for_lossless_round_trip`, `Partially_valid_link_array_remains_raw_for_lossless_round_trip`, `Load_save_normalization_is_idempotent_and_keeps_exact_canonical_roots` | Invalid documents retain original raw property content for correction; repeated normalization is identical and does not introduce a second top-level Links representation |
| Editor expression scope | `Load_and_save_preserve_expression_selection_and_condition_parameters` | Application and workflow evaluator names plus exact conditional parameter survive serialization/reload |
| Readiness while updating | `Active_revision_stays_healthy_and_operational_while_candidate_reload_is_held_in_preparation` | Held candidate leaves active revision healthy and serving; real send/receive preserves value/header identity and message ID; release applies candidate |
| Rejection and recovery readiness | `Rejected_reload_with_active_revision_reports_degraded_and_preserves_active_revision`, `Successful_update_after_rejection_restores_healthy_readiness` | Failed update retains active definition/revision; status degrades then recovers; obsolete diagnostic fields disappear |
| Safe, read-only health data | `Health_check_data_is_bounded_uses_only_the_last_diagnostic_and_excludes_sensitive_details`, `Health_check_observation_does_not_mutate_application_state_revision_ports_or_last_update` | Exact bounded field set, last diagnostic code/stage only, no secret/details/exception leakage; repeated observation leaves runtime identities and state unchanged |

## Source ownership and interpretation

Current `FlowJson.BuildAsync` constructs ordinary Core components, links them, and
validates the graph before returning it. It disposes failed construction and
explicitly refuses unsupported resource handling in the minimal overload.
Resource-capable construction remains in optional Composition; its lifetime
qualification is linked from the portable-resource review.

`ApplicationConditionCompiler` is owned by Json and has one preparation-session
cache keyed by engine selection, expression, input type and parameter signature.
Compiled conditions, not source strings, execute on message delivery. Reflection
for the optional typed expression context is preparation work, not forwarding work.
The CSharp adapter's compiled object stores the native parsed body; the Jsonata
adapter's compiled object stores the native query. Calling the separate one-shot
Evaluate API does parse, by design; do not misrepresent it as the cached link path.

`DesignerApplicationPersistence` projects the existing portable model and uses the
existing link compiler; it does not run a second graph. Health observes optional
runtime state and fails closed if its state/current/update reads change during
observation. The tested readiness snapshot does not initiate application startup.

Host-native functions remain trusted in-process code. They must be thread-safe and
can perform work outside evaluator control; these tests do not establish sandboxing
or preemptive cancellation of synchronous host functions. Per-evaluation native
token isolation is distinct from the prohibited per-branch payload cloning in
graph fanout.

## Disposition and remaining scope

The named authoring, evaluator configuration, editor persistence and health
requirements above now have current-source, assertion-level and execution evidence.
The larger feature ledger still needs reconciliation of remaining component-family
operations and the other approved concurrency/transport boundaries. It must not
reopen these specific checks merely because a historical audit called them pending.

This is not complete event-stream parity for every component, visual editor UI
acceptance, native engine security certification, sustained condition throughput,
network integration or full refactor completion. Existing tracing/event tests and
package consumers retain their separately stated scope. No new library dependency,
API, compatibility path or context-aware Add overload was introduced. Nothing was
published or sent to an external consumer task.
