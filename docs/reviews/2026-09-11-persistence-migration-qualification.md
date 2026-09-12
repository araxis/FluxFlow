# Persistence migration qualification

The storage component family and local durability implementations retain their
verified operations after the canonical-runtime migration. Server-database runtime
conformance remains an explicit external prerequisite, not a passed test.

## Source changes and stored formats

The reproducible audit compares all 53 original C# files in the six persistence
provider directories with the captured baseline. None had pre-existing edits in
the original dirty-worktree manifest. Forty-three files are unchanged; ten differ
only in reviewed addressing-namespace references. Nine use a relocated using
directive; one contains a fully qualified address parser call. No provider C# file
was added or removed. The comparison does not normalize arbitrary text or SQL.

Consequently, the provider-local queries, state transitions, schemas and existing
migrations were not rewritten by this refactor. Provider projects retain the real
database/client libraries and both supported targets. Current provider C# sources
contain the schema definitions; no separate SQL schema files were found in these
families. This source comparison is not a claim that unchanged code is bug-free.

The meaningful shared-boundary changes were reviewed separately:

- Durable input validates the registered payload contract, then restores the
  original persisted JSON as a canonical value. It does not reserialize the
  materialized object and lose extension fields or custom wire names. A full
  addressed input returns a rejection for the existing durable retry policy.
- Durable output captures the canonical value's existing JSON representation.
  It no longer requests a typed output port or repeats a caller's serializer.
  Capture registration keeps address and contract name; the typed serializer
  argument and duplicate payload-type registry are removed.
- All four storage nodes now expose their existing pipeline's local `FlowPorts`
  and implement `IFlowComponent`. Their store operations are unchanged, and
  constructor dependencies remain explicit and caller-owned.

The breaking usage changes are already described in
`docs/46-code-first-major-migration.md`. This checkpoint introduces no new schema
migration, no data rewrite, and no library source change.

## Fresh executed suites

All runs used the current Release build on .NET 10, with `--no-build --no-restore`
and fresh TRX output. The immediately preceding nonincremental full-solution build
was clean, recorded in `consumer-signals-20260911/qualified/solution-build.log`.
All 1,488 qualified source inputs stayed unchanged. An independent check matched
all 22 source-output DLLs for the eleven persistence-related packages, across
both supported targets, to the startup.1 package inventory.

| Test project suffix | Passed |
|---|---:|
| Engine.DurableInput.Tests | 163 |
| Engine.DurableOutput.Tests | 187 |
| Engine.DurableInput.SqlFile.Tests | 127 |
| Engine.DurableOutput.SqlFile.Tests | 166 |
| Components.Storage.Tests | 37 |
| Components.Storage.SqlFile.Tests | 31 |
| Components.Storage.FileSystem.Tests | 30 |
| Components.Storage.Adapters.Tests | 4 |
| Components.Storage.Composition.Tests | 21 |
| Total | 766 |

Zero executed cases failed or were skipped. The embedded provider fixtures create
actual temporary database files, instantiate the production stores, reopen them,
and query persisted rows. Inherited conformance cases override store creation with
those providers; they are not merely passing against an in-memory substitute.
Shared dispatcher tests also use controlled stores/clocks where they must inspect
exact calls and race boundaries. Those tests are not represented as database tests.

## Capability-to-assertion ledger

These representative assertions were inspected in source and their exact method
names confirmed in the fresh passed results. The complete execution list is in
the audit's `tests.json`.

| Capability | Concrete evidence |
|---|---|
| Instance-first put/get/query/delete and borrowed store | `Direct_storage_graph_processes_all_operations_without_owning_store`: four constructors, local port identity, exact bytes, all operations, error forwarding, lineage, drain, zero store disposal calls. |
| Real local content persistence | `Canonical_nodes_round_trip_exact_content_across_store_instances`: reopened store returns exact bytes, content type and encoding; executed for both embedded and file providers. |
| Persisted wire shape on input | `Restored_payload_keeps_fields_not_materialized_by_the_contract`: exact stored JSON and all message identity fields survive restoration; related cases exercise naming policy, generated metadata and custom converter invocation counts. |
| No repeated serialization on output | `Capture_preserves_custom_ingress_serializer_shape_without_running_it_again`: one writer call, zero reader calls and exact captured JSON; both ingress configuration paths run. |
| Explicit workflow acknowledgement | `Subscription_precedes_send_and_explicit_success_is_the_only_delivery_boundary`: no delivery transition before explicit completion, exactly one afterward, subscription disposed once. |
| Idempotency and scheduled retry | `Enqueue_is_idempotent_by_address_and_message_id_without_overwrite` and `Release_defers_retry_until_the_explicit_next_attempt_time`: original content retained, conflicts distinguished, retry excluded one tick before its due time. |
| Lease ownership and recovery | `Active_lease_remains_exclusive_after_reopen_and_expires_at_the_exact_boundary` and `Expired_lease_recovers_with_new_token_owner_exact_times_and_next_attempt`: exclusive ownership, fresh token/owner/attempt at expiry, stale settlement rejected. |
| Input dead-letter replay | `Replay_preserves_envelope_resets_operational_state_and_increments_generation`: content retained, schedule respected, attempts reset, second generation advances, stale replay leaves details unchanged. |
| Output dead-letter replay | `Replay_preserves_envelope_generation_and_schedule_then_next_lease_is_attempt_one`: exact reply status, terminal inspection clears, no early lease, retained envelope and reset attempt. |
| Existing schema migration | `Version_one_rows_migrate_to_version_two_with_state_and_envelope_preserved`: pending/released/leased/delivered/dead rows, metadata, exact schema version and index survive the existing migration. |
| Safe retention | `Delivered_purge_uses_exclusive_utc_cutoff_and_preserves_every_other_state` and `Completed_purge_uses_exclusive_utc_cutoff_and_preserves_every_other_state`: only the expired terminal row is removed; equal/newer and other states remain, including capture-only output rows. |

## Artifacts and remaining limits

`artifacts/core-refactoring/persistence-audit-20260911/verify.ps1` performs the
provider source comparison, runs the nine projects, validates their TRX counters
and records source stability. `provider-source.json`, `commands.json`, `tests.json`,
the original TRX files and `verification.json` preserve the evidence. The verifier
refuses to overwrite a completed audit.

Current read-only prerequisite checks found no configured external database
connection and no available container-engine pipe. No container was started, no
license was accepted, and no remote database was contacted. The server provider's
source relocation is verified, but its real concurrency, persistence and recovery
suites remain unrun here. Existing package-only restart acceptance remains separate
evidence; these fresh 766 cases are source-project tests, not new package-only
restart runs or new .NET 8 conformance runs.

The storage family now has explicit operation/ownership evidence, and local
durability has provider-backed evidence for the listed required capabilities.
This does not complete other component families, the remaining performance matrix,
external integrations, or the full refactoring handoff. The local candidate stays
`0.0.0-local.20260911.startup.1`. Nothing was published or sent to another task.
