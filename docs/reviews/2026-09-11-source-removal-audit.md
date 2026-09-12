# Source removal and replacement audit

The baseline source-path removal ledger is complete for its stated scope:
83 missing paths have existing replacements or explicit retirement dispositions.
This closes the missing file-accounting item from the capability audit. It does
not turn path correspondence into exhaustive feature-parity approval.

## Baseline and identity

The audit combines the captured starting commit
4d11bd889679aa40970be18f8d4e0772732f2137 with the captured initial working-tree
status and changed/untracked file hashes. No initially deleted source paths were
found. Of the 83 removed paths, 81 were tracked and two were initially untracked:
ApplicationConditionCompiler.cs and ConditionParameters.cs. Both now exist under
the optional JSON project's Links directory. A Git-only deletion list would have
missed them.

Eight removed paths had captured starting changes. Their comparison uses the
captured raw SHA-256, not the older commit blob. The audit does not reconstruct
their original contents or claim their original working edits were identical to
the commit. For other paths, Git-normalized content hashes provide a precise
line-ending-normalized identity check. Eighteen replacements match exactly by
that check. The remaining rows are deliberately not labeled unchanged moves;
raw-hash differences alone also do not establish a semantic difference.

The scope includes every missing tracked baseline source path and every missing
captured changed/untracked source path. It excludes transient files introduced
and removed during execution, tests and documentation outside src, and public
member removals inside surviving files. Those are separate migration concerns.

## Replacement responsibilities

The machine-readable ledger contains one row per old path, baseline provenance,
all replacement paths and hashes, a disposition, and test/review navigation.
The following groups cover all 83 rows:

| Removed area | Paths | Current responsibility |
| --- | ---: | --- |
| Foundation package | 27 | Core Nodes/Data; canonical FlowNode/FlowSource replace duplicate value facades; native controls replace the signal facade; retired package and compatibility suppression have explicit breaking dispositions |
| Coordination package | 8 | Core Coordination contains the shared keyed exchange implementation; old package identity is retired |
| Composition addressing/model/link files | 17 | Addressing belongs to Core; portable model, link syntax and condition compilation belong to Json, including the two initially untracked files |
| Composition runtime and typed-port visitor | 4 | FlowApplication/FlowExecution/FlowComponent plus direct contract-to-Core construction; canonical declared ports no longer require a typed visitor |
| Component wrappers/helpers | 20 | Direct source, retry, timer, routing and observability components reuse canonical execution; request tracker has an explicit keyed-completion migration; editor metadata moves to the family Designer package |
| Engine duplicate input/output/fanout files | 7 | Canonical stable input/output owners retain operational revision behavior and reuse Core dispatch |

The removed wrapper-declaration scan found no matching declarations for its
explicit list of retired pipeline, facade, tracker and runtime-wrapper types.
This is a lexical check, not a universal duplicate-runtime detector. The prior
source-backed execution-owner audit remains the evidence for execution ownership.

## Source and behavior review

Current sources confirm the following boundaries:

- FileWatchNode and the transport event source inherit FlowSource. Subscription
  and watcher work starts through source production; cleanup remains local.
- The subscription trigger's Ack/Nak controls are bounded native canonical inputs.
  Retry likewise declares actual Input/Ack/Nak/Cancel ports. Removing the old
  signal facades does not remove acknowledgement and rejection behavior.
- HttpTriggerNode delegates exchange ownership to the shared request bridge and
  retains typed reply validation at its transport boundary.
- Routing nodes retain pending business state and timing stages locally while
  using canonical node transport. Generic payload arguments do not introduce
  another typed application-routing path.
- Observability nodes materialize payloads locally and emit canonical messages
  through FlowNode; their removed local pipeline/facade is not required.
- ContractApplicationExtensions constructs FlowApplication, adds activated
  instances, connects canonical endpoints and disposes failed construction.
  Optional contract activation remains outside Core and is not required by direct
  component constructors.
- Stable engine outputs retain revision attachments and observations while using
  Core FlowOutput dispatch. Operational staging is not asserted to have the same
  queue layout as direct application execution.

The public tracker retirement remains an intentional breaking migration, not a
same-signature replacement: keyed callers await shared exchange completion and
own their callback tasks; the transport bridge owns transport notification work.
The existing tracker-retirement-migration document maps the removed tracker tests
to surviving assertions and describes the deliberate ownership changes.

## Fresh verification

| Suite | Framework | Passed |
| --- | --- | ---: |
| Transport components | net10.0 | 121 |
| Observability components | net10.0 | 25 |
| Routing components | net8.0 | 98 |
| Routing components | net10.0 | 98 |
| File-system components | net10.0 | 51 |
| Host request trigger | net10.0 | 22 |
| Total | | 415 |

All six reports have zero failures/skips. These executions overlap the existing
4,839-pass qualified local suite; do not add them to create a new unique total.
They use the previously qualified Release outputs without rebuilding.

Assertions inspected include:

- Join_runs_as_a_direct_component_with_two_canonical_inputs: exact native port
  identity, addresses, request/reply result and left/right payloads.
- Join_reports_invalid_materialization_then_matches_valid_inputs and the bounded
  input/fault cases: exact error/result count, ordered pending outcomes, admission
  blocked until release, disposal waiting for the active selector.
- Counter_output_fans_out_every_snapshot: two count values and identical envelope
  references received by both sinks; predicate failure remains in-band and later
  processing continues.
- Trigger_return_control_link_drains_work_and_broker_outcome_before_application_stop:
  both Ack/Nak cases, stop waits for processing and acknowledgement callbacks,
  exactly one outcome, completed control inputs and borrowed controller retained.
- Direct_trigger_preserves_transport_validation_and_continues_after_rejection:
  invalid reply shape, diagnostic code, subsequent binary request/header/body
  preservation, response status/body and complete graph drain.
- Direct file-source tests exercise actual local enumeration/watcher behavior,
  declared canonical ports and completion/cleanup. They are not broker tests.

The transport suite uses a recording transport, and the host trigger uses an
in-process request context. This pass does not claim real broker/network
integration. The previously reported unavailable container prerequisite remains
unchanged; it was not rechecked or started here.

## Reproducibility and remaining scope

Artifacts are under artifacts/core-refactoring/deletion-audit-20260911:

- inventory.ps1 generates deletions.json and summary.json from the captured
  baseline and current files. It fails on an unmapped path or missing replacement
  or evidence location.
- The five suite folders contain six fresh TRX reports.
- verify.ps1 and verification.json check all replacement hashes, test outcomes,
  1,486 qualified source inputs, 81 unchanged local candidate archives and 162
  unchanged target assemblies. The current candidate remains t1430.

No production code, package archives, test assertions or baseline artifacts were
changed by this audit. Nothing was deleted during this pass. Prior removed source
remains recoverable from Git where tracked; initial working-change patches and
hashes remain in the captured baseline. Hash-only untracked captures should not
be mistaken for a recoverable copy of their original contents.

Remaining goal gates are complete capability/public-member migration signoff,
the outstanding telemetry/performance matrix, available real integration evidence
and the consolidated final handoff. The goal remains active. No publication or
external consumer coordination is authorized by this checkpoint.
