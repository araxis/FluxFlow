# Component behavior qualification: seven local families

Reviewed families: Serialization, Assertions, Payloads, Validation, State,
Projections and Sessions. Their runtime/configuration behavior is reconciled below
against the captured source baseline and current executed assertions. This closes
these seven local family rows, not the remaining families, external integrations
or performance acceptance. No production or test C# changed during this review.

## Baseline comparison

The inventory covers **all 92 C# files** in these seven runtime package folders,
not only files named Node. None was dirty in the captured starting worktree, so
comparison with commit `4d11bd889679aa40970be18f8d4e0772732f2137` is valid here.
Content comparison normalizes line endings and terminal newlines only.

| Family | Unchanged files | Changed files |
| --- | ---: | ---: |
| Serialization | 6 | 7 |
| Assertions | 5 | 1 |
| Payloads | 7 | 0 |
| Validation | 9 | 0 |
| State | 11 | 0 |
| Projections | 13 | 0 |
| Sessions | 31 | 2 |
| Total | 82 | 10 |

No runtime C# files were added or removed in these folders. Eight wrapper changes
are exactly `IFlowNode` → `IFlowComponent` plus forwarding `Ports` to the existing
pipeline: six serialization nodes and session recorder/query. The serialization
pipeline's only change returns the original incoming error message, preserving its
identity instead of creating a derived error. All nine transformations were checked
against exact expected source replacements, not accepted merely because diffs are small.

The remaining change consolidates Assertions: the generic component now processes
the canonical envelope, materializes locally, emits a canonical result and forwards
the original upstream error. JsonAssertionNode now specializes that implementation
instead of duplicating evaluation, diagnostics, options and transport behavior.
The complete diff was inspected. The assertion tests below exercise typed and
JSON-local results, invalid materialization, custom context, diagnostics and recovery.

The source inventory excludes optional Composition/Designer projects; those have
intentional registration/editor relocations, not unchanged-source claims. Their
package/constructor/configuration accounting is in the preceding migration reports,
and all seven Composition suites were included in the current executions.

## Current qualification

| Family | Runtime tests | Composition tests |
| --- | ---: | ---: |
| Serialization | 17 | 22 |
| Assertions | 11 | 15 |
| Payloads | 17 | 13 |
| Validation | 10 | 15 |
| State | 18 | 15 |
| Projections | 23 | 12 |
| Sessions | 49 | 26 |
| Total | 145 | 118 |

**263 passed**, zero failed/skipped, across 14 net10.0 projects. Exact commands,
test names, hashed reports, candidate dependency copies and source snapshot are in
`artifacts/core-refactoring/component-behavior-20260911/qualified`.
`runtime-source.json` and `baseline.ps1` in its parent record all 92 comparisons.

The current candidate is `0.0.0-local.20260911.startup.1`. All 1,488 qualified source
inputs stayed unchanged. All 79 loaded production dependency copies match its
target DLL hashes; shared test-support copies were separately checked against
their explicitly referenced support-project output. The initial verifier stopped
after 17 passed cases because it incorrectly treated that test helper as a package.
Its partial evidence is preserved in the parent folder; the corrected complete
batch is under `qualified`. No test failure or library change was hidden by that
correction.

These were fresh executions of previously clean Release outputs using the detected
xUnit/VSTest runner, `--no-build --no-restore --framework net10.0`. They overlap the
4,848-pass regression and must not be added to its unique total. They do not imply
new net8 source-suite executions or a new solution build.

## Capability-to-assertion map

| Family / behavior | Inspected executed test | Concrete evidence |
| --- | --- | --- |
| Six serialization converters in an ordinary graph | `Direct_serialization_graph_round_trips_all_converters_and_forwards_errors` | Actual constructor/Add chain; exact text and UTF-8 bytes; original error at both sinks; trace/correlation and derived causation; all components drain |
| Failed Add ownership | `Direct_rejected_add_leaves_component_usable` | Duplicate name rejects without completing the caller's instance; same instance then works as a borrowed component |
| Binary and error conversion | `Base64_RoundTripPreservesExactBytes`, `InputError_IsPropagatedWithoutConversion`, `Output_FansOutSameOwnedJsonValueWithoutReparse` | Exact 0/1/2/255 bytes and encoding; original error envelope; same output envelope at both consumers |
| Conversion limits and events | `LimitsProduceErrorData`, `SuccessAndFailureEmitDiagnosticEvents` | Oversized input returns the expected error; success/failure event names are preserved |
| Optional serialization bindings | `Serialization_standard_bindings_preserve_native_identity` | All six bindings expose the actual canonical ports, construct once per case and dispose to successful completion |
| Assertion domain outcomes | `Passed_and_failed_assertions_are_typed_results`, `Json_runtime_facade_uses_canonical_ports_and_preserves_lineage` | Pass and failed assertion are normal typed outcomes, not conflated with evaluation error; input/options/result content and lineage survive |
| Assertion local materialization/recovery | `Assertion_reports_invalid_materialization_and_evaluates_next_input`, `Assertion_forwards_the_same_error_without_evaluating_it` | Bad shape returns the expected evaluation error without calling predicate; next input evaluates once; upstream error remains the identical envelope |
| Assertion configuration/diagnostics | `Context_factory_receives_materialized_typed_value`, `Events_and_results_use_injected_clock` | Custom context receives the local typed value; exact timestamp, level, result kind, expression ID and error flag |
| Declared payload interpretation | `Keeps_unknown_media_as_exact_binary_without_content_sniffing`, `Rejects_oversized_content_before_decoding` | Binary content is not inferred as JSON; exact bytes preserved; size limit wins before parse failure |
| Payload errors/recovery/events | `Emits_invalid_declared_json_as_an_in_band_error_and_continues` | Expected parse error/details, subsequent valid content, failure/success event order and successful completion |
| Schema validation outcomes | `Valid_and_invalid_json_values_are_normal_domain_results`, `Selector_returns_the_transport_neutral_selected_json_value` | Invalid schema result remains domain data with issues; exact original/selected JSON and selector invocation count |
| Schema selector errors | `Selector_failure_is_in_band_and_later_input_continues`, `Incoming_error_is_propagated` in validation | Exact selector error and recovery; original upstream error object preserved |
| Keyed state order and identity | `Reduce_preserves_order_typed_state_and_message_lineage` | Independent keys, exact previous/new state, versions, injected time, trace/correlation/causation |
| Reset/clear and rejected operation | `Reset_and_clear_are_successful_domain_results`, `Unsupported_operation_is_in_band_and_later_input_continues` | Exact state/version transitions, reinitialization after clear, invalid operation without poisoning later work |
| State capacity and failed reduction | `Key_expression_and_key_limit_use_typed_input_variables`, `Reducer_failure_is_in_band_and_later_input_continues` | Configured key selector and bounded key count; rejected reduction does not commit state/version |
| Hosted state configuration | `Canonical_host_emits_reset_clear_and_normal_failure_results` | Same reset/clear/error/recovery behavior through configured canonical ports |
| Projection filtering/rates/identity | `Matching_events_emit_ordered_snapshots_with_message_lineage`, `Filter_applies_exclusions_identity_attributes_and_time_range` | Exact observed/matched counts and rate, preview truncation, lineage; excluded time/channel cases do not match |
| Projection ownership and final output | `Projection_node_preserves_contract_owned_attributes_in_filter_and_snapshot_behavior`, `Completion_emits_one_final_snapshot_after_accepted_input` | Caller dictionary mutation cannot change configured/filter/result data; read-only snapshots; exactly one final snapshot after accepted work with last-message causation |
| Session record/query/ownership | `Direct_session_graph_records_queries_and_finalizes_without_owning_store` | Constructor-based canonical graph; errors do not touch store; record/query exact result; session finalizes on drain; borrowed store remains undisposed and usable |
| Session sequence and append recovery | `Recorder_preserves_content_lineage_order_and_existing_sequence`, `Recorder_propagates_input_and_append_failures_as_data_and_continues` | Existing sequence continues at 6/7, exact content/header/lineage and final count; append failure stays in-band and later append succeeds |
| Session replay | `Replay_emits_exact_content_in_order_and_mints_message_identity`, `Replay_supports_fixed_interval`, `Replay_supports_multiplier_timing`, `Replay_cancellation_completes_without_late_output` | Start/max selection and exact sequence/content, independent trace identities, configured timing modes, cancellation with no later queued output |
| Session query | `Query_returns_filtered_result_with_clock_and_lineage`, `Query_can_omit_sessions_without_changing_count`, `Query_returns_invalid_request_as_data_and_continues` | Filters/options passed to store; selected result, exact count/time/lineage; optional omission of result details; invalid query does not poison following request |

Timing tests use existing injected-clock/barrier helpers where supplied. Their
counts and configured timing behavior are not p99 or sustained scheduling-precision
measurements. Session store tests use the repository test store: they verify the
component/store contract, not an unconfigured external session-store implementation.

## Decision

Preserve the unchanged processing implementations and the small reviewed transport
changes. No new per-family executor, factory hierarchy, compatibility alias or
test-driven rewrite is justified by this evidence. Ordinary component construction
remains direct; optional configuration bindings and editor declarations stay outside
Core. The explicitly bounded state-operation dispatch remains local business logic,
not a central component-type switch.

This reconciles these families against their captured runtime source and the named
behavior assertions. Shared runtime changes retain their separate concurrency,
identity and lifetime qualification. Remaining family rows, protocol/provider
environments, the approved performance gates and final consumer acceptance remain
open. Nothing was published or sent to an external consumer task.
