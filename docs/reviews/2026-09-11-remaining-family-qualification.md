# Remaining component-family qualification

This reconciles local behavior for the eleven remaining ordinary families plus
the request bridge, inbound HTTP wrapper and transport adapters. Together with
the seven-family and storage/persistence qualifications, every ordinary family
now has a local behavior disposition. External protocol/provider integration and
the agreed performance gates remain open; this is not overall release approval.

No production/test C# changed. Candidate: `0.0.0-local.20260911.startup.1`.

## Source and replacement inventory

All C# files in the twelve selected runtime family folders and three transport
adapter folders were compared with the captured starting commit. None was dirty
in the initial working-tree snapshot. Comparison normalizes line endings and
terminal newlines only; it does not normalize away code differences.

| Family (including selected adapters) | Unchanged | Changed | Removed |
| --- | ---: | ---: | ---: |
| Expectations | 8 | 0 | 0 |
| FileSystem | 26 | 4 | 1 |
| Http | 10 | 3 | 1 |
| Mapping | 7 | 1 | 0 |
| Metrics | 11 | 1 | 0 |
| Mqtt | 49 | 3 | 2 |
| Observability | 13 | 3 | 2 |
| Resilience | 9 | 2 | 2 |
| Routing | 16 | 5 | 4 |
| Sources | 5 | 2 | 0 |
| Timers | 12 | 6 | 5 |
| RequestReply | 0 | 3 | 2 |
| Total | 166 | 33 | 19 |

All 218 original paths are accounted for; no new runtime C# paths were found in
this selection. This table is source accounting, not automatic equivalence for
the 33 changed files. The preceding source-removal/execution-owner reviews and
the current behavior assertions below establish the replacement responsibilities.

All 19 removals were matched uniquely to the existing deletion ledger; replacement
files and evidence paths were checked again and current hashes recorded. They
cover local source pumps, typed/canonical observation and routing wrappers, timer
facades, signal adapters and the retired request tracker. The shared Core runtime
and component-local operation state replace them; file removal is not permission
to remove timers, acknowledgement, join/window outcomes or transport callbacks.

Current source inspection confirms that generated/sequence sources now directly
inherit FlowSource; the useful generated-source static factory detaches JSON
before caller document disposal. Native configuration adapters remain separate.
The metric aggregate's additional code traces final emission through the shared
Core helper and clears the retained last-sample carrier; it does not replace the
aggregation algorithm or introduce a reporting service. See the existing
[final-snapshot trace review](2026-09-11-metric-final-tracing.md).

The [constructor/configuration migration](2026-09-11-component-contract-migration.md)
and [source-removal review](2026-09-11-source-removal-audit.md) retain the explicit
breaking dispositions. In particular, inbound HTTP submits contexts through
Incoming, and explicit-key tracker callers migrate to shared coordination rather
than receiving a compatibility implementation of the removed tracker.

## Fresh local execution

| Family | Runtime tests | Composition tests | Additional adapter/bridge tests |
| --- | ---: | ---: | ---: |
| Expectations | 16 | 17 | — |
| FileSystem | 51 | 26 | — |
| Http | 25 | 17 | 22 inbound HTTP |
| Mapping | 12 | 16 | — |
| Metrics | 51 | 14 | — |
| Mqtt | 121 | 40 | 7 common adapters + 12 first transport + 10 second transport |
| Observability | 25 | 24 | — |
| Resilience | 35 | 8 | — |
| Routing | 98 | 17 | — |
| Sources | 39 | 40 | — |
| Timers | 77 | 14 | — |
| RequestReply | 48 | — | — |

Total: **882 passed**, zero failed/skipped, 27 net10.0 projects. All 1,488 qualified
source inputs stayed unchanged. All 149 loaded production dependency copies match
the candidate's target DLLs. Explicitly referenced shared test-support copies
were checked against their support-project output; runtime DLLs stayed unchanged
during execution. Metrics/Routing also declare net8, but this targeted batch
explicitly selects net10 and does not claim new net8 executions.

Evidence is under `artifacts/core-refactoring/remaining-family-behavior-20260911`:
`qualified` contains exact commands, logs, every test name, hashed TRX reports,
dependency hashes, source snapshot and verification summary. `runtime-source.json`
contains all 218 comparisons; `replacements-current.json` contains all 19 current
replacement dispositions. The generating/verifying scripts refuse to overwrite
completed reports. These fresh executions use previously clean Release outputs
with no build/restore and overlap the existing 4,848-pass regression.

## Inspected operation assertions

| Capability | Executed test evidence | What was checked |
| --- | --- | --- |
| Expect/guard, timeout and completion | `Expect_match_is_normal_result_with_message_lineage`, `Guard_match_is_normal_unmet_result`, `Timeout_is_normal_result`, `Completion_drains_input_then_emits_one_normal_result` | Exact domain outcome, lineage and diagnostics; opposite expect/guard timeout semantics; accepted input drains before one completion result |
| Expectation settlement race | `Match_wins_once_over_later_timeout_and_completion`, `Concurrent_timeout_and_completion_emit_exactly_one_result` | Later timeout/completion cannot duplicate a match; concurrent terminal paths emit one result and complete |
| Direct file I/O | `Direct_file_graph_reads_and_writes_exact_bytes_and_forwards_errors` | Real temporary file bytes 0/127/255/0, exact metadata and lineage, original error envelope, full graph drain |
| File sources | `Direct_directory_source_uses_canonical_ports_and_drains_entries`, `Direct_file_watch_stops_or_faults_and_releases_watcher` | Actual directory filtering/lengths; watcher events, original fault, completion and cleanup; no source-local execution facade required |
| HTTP content and policy | `Request_and_response_preserve_exact_content_and_message_lineage`, `Configured_non_success_response_is_an_error_result_and_later_input_continues`, `Response_body_is_bounded_and_keeps_content_metadata` | Exact request/response bytes, headers, charset and lineage; configured status error then recovery; bounded response body with truncation flag |
| Incoming HTTP | `Direct_trigger_preserves_transport_validation_and_continues_after_rejection` | Direct constructor/ports; invalid response shape produces expected status/diagnostic, subsequent request preserves method/path/header/binary body and exact reply; complete drain |
| Mapping and backpressure | `Maps_typed_values_and_preserves_message_lineage`, `Output_delivers_all_ordered_results_to_two_subscribers_under_backpressure` | Typed local input/output and derived identity; postponed sink blocks admission/drain, then all four results reach both sinks in order as identical envelopes |
| Mapper recovery | `Mapper_reports_invalid_materialization_and_maps_next_input`, `Mapper_forwards_the_same_error_without_evaluating_it` | Invalid shape does not call mapper; later valid input maps once; upstream error bypasses evaluation and keeps its envelope |
| Explicit metric processing | `Samples_emit_ordered_snapshots_with_groups_rates_and_lineage`, `Invalid_sample_is_normal_failure_and_later_sample_continues`, `Group_limit_failure_carries_updated_global_snapshot` | Exact totals/counts/average/rate/groups and lineage; invalid sample recovery; bounded-group failure code and rejected group. These are explicit component outputs, not consumer dashboard ownership |
| Transport commands | `Command_runs_as_a_direct_component_without_owning_controller`, `Command_expected_failure_is_error_data_and_does_not_stop_following_requests`, `Command_forwards_error_envelope_without_executing_controller` | Actual direct component path, exact command topic/lineage, borrowed controller retained; expected transport error/recovery; upstream error causes no publish |
| Transport source lifetime | `Transport_sources_are_direct_components_and_subscribe_only_on_start`, `Source_fault_joins_active_enumeration_before_disposing_subscription` | No pre-start subscription; exactly one registration, configured capacity, declared ports; fault waits for active enumeration and disposes subscription once without disposing controller |
| Trigger acknowledgement drain | `Trigger_return_control_link_drains_work_and_broker_outcome_before_application_stop` | Both Ack/Nak addressed return paths; stop waits for active work and held acknowledgement callback; exact one outcome, closed inputs, unsubscribe and borrowed connection retained |
| Adapter wire mapping | `ToApplicationMessageMapsCanonicalContentAndMetadata`, `ToPublishPacketMapsCanonicalContentAndMetadata`, `ToReceivedApplicationMessageMapsCanonicalContentAndMetadata` | Both native mappings preserve exact bytes, content type, QoS, retain, response topic, correlation and named user properties |
| Adapter transport options | `Session_maps_all_four_portable_modes_to_exact_mqttnet_channel_options`, `Session_maps_all_four_portable_modes_to_exact_pulse_transport_options` | TCP/TLS and WebSocket/secure WebSocket options, IPv6/path encoding, TLS target names and client certificate settings; not a real TLS handshake claim |
| Counter/logger/metric components | `Component_runs_directly_with_canonical_ports_and_request_reply`, `Invalid_materialization_is_in_band_and_does_not_advance_state`, `Incoming_error_keeps_its_envelope_and_bypasses_selectors` | All three operations use direct ports/request-reply; malformed input does not advance ordinal/state or call selector; exact upstream envelope preserved |
| Active observation work | `Bounded_input_backpressures_until_the_active_selector_returns` | Second admission remains pending while first selector is held; release gives exact count and total, followed by drain |
| Retry native inputs | `Direct_component_uses_native_business_ports_and_preserves_message_data`, `Upstream_error_is_forwarded_without_starting_an_attempt` | Input/Ack/Nak/Cancel are actual bounded native business inputs; exact structured/binary value and lineage; error feedback payload is irrelevant; all inputs drain |
| Retry attempts/races | `Nak_schedules_next_attempt_and_late_feedback_is_rejected`, `Attempt_timeouts_retry_then_exhaust_exactly_once`, `Competing_feedback_settles_the_attempt_exactly_once` | Configured retry delay, fresh attempt sequence and causation; stale feedback ignored; timeout exhaustion and competing terminal feedback do not duplicate completion |
| Routing outcomes | `Window_emits_count_result_with_message_lineage`, `Correlation_emits_match_and_timeout_on_one_output`, `Join_emits_match_and_timeout_on_one_output`, `Join_selector_failure_is_normal_error_result_with_message_lineage` | Ordered window items; exact request/response and left/right match data; timeout stays a normal outcome; selector error preserves its diagnostic/lineage |
| Source construction and ownership | `Sources_are_direct_Core_components_with_actual_ports_and_reference_fanout`, `Generated_factory_detaches_json_items_before_document_disposal` | Both source kinds produce exact sequence 3/5/7 through direct graph/fanout; shared envelopes and disposal; static factory survives disposed input JSON for scalar/array/object/null |
| Source stop | `Direct_source_stop_cancels_timing_and_drains` | Initial/interval timers are controlled, stop drains exactly one emitted item and prevents later output after virtual time advances |
| All timer shapes | `Direct_timer_sources_and_operators_use_canonical_ports`, `Direct_timer_chain_preserves_binary_and_error_identity` | Interval/schedule/delay/throttle/debounce in direct graphs; scheduled due time, original binary/error envelopes, no leftover outputs/timers |
| Timer terminal behavior | `Direct_debounce_stop_flushes_only_latest_input`, `Direct_delay_fault_joins_scheduled_work` | Stop emits latest pending value once; fault preserves original cause, cancels scheduled work and leaves zero active timers |
| Bridge startup/drain | `Direct_bridge_waits_for_start_and_drains_responses_on_stop`, `Bridge_pre_start_shutdown_settles_every_admitted_context` | No context read before start; independent exchange identity; stop keeps response input open for active work; pre-start stop/fault settles each admitted caller exactly once |
| Bridge response boundary | `Direct_bridge_materializes_binary_reply_once`, `Direct_bridge_invalid_reply_fails_only_its_caller` | Exact binary content and one conversion; bad reply fails only its caller and later request succeeds; pending count returns to zero |

## Integration limits and disposition

File tests use real local files/watchers. Outbound HTTP tests use controlled
handlers; inbound HTTP uses in-process request contexts. Transport controller
tests use recording sessions, one adapter uses a recording native client, and the
other includes its in-process test broker. These are meaningful boundary checks,
but do not substitute for the configured container broker/socket workflow or
external server-provider suites. No unavailable environment was counted as passed.

The full local family ledger is now represented by this report, the
[seven-family review](2026-09-11-component-behavior-qualification.md), and
[storage/persistence review](2026-09-11-persistence-migration-qualification.md).
Constructor/options, dependency and file-removal accounting remain separate
supporting evidence. Do not reopen these local checks solely because earlier
checkpoint documents marked them pending; do not turn their completion into
blanket performance, remote-service or whole-framework signoff either.

Next work is the remaining cross-cutting acceptance/performance evidence and
real integration prerequisites, followed by the final source/candidate audit.
No new creator hierarchy, evaluator, iteration feature, context-aware Add overload
or compatibility path was added. Nothing was published or sent to another task.
