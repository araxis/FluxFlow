# Concurrency Reliability Hardening

Date: 2026-08-03

## Outcome

The four isolated failures observed during the canonical release train were
diagnosed and corrected at their actual boundaries without changing production
code or published package behavior.

- The two T-SQL durable-input failures were caused by an integration assertion
  that required equal `5 + 5` batches from simultaneous callers. The public
  request exposes `MaxCount`, and the provider explicitly uses `READPAST` for
  cooperative skip-locked queue leasing. A caller may therefore receive fewer
  rows, including zero, while another transaction owns candidate row locks.
- The component-event and request/reply timing failures published into a
  `FlowOutput<T>` before registering the receiver. The output intentionally
  snapshots active subscribers and does not replay items accepted when no
  subscriber exists.

## T-SQL Contract And Regression Proof

Production SQL, schema version 1, locking-read-committed requirement,
eligibility index, provider dependencies, and public contracts remain
unchanged.

The multi-owner integration test now proves the actual lease guarantees:

- each result is bounded by its requested maximum;
- every returned row has the requested owner and a unique token;
- simultaneous owners never receive the same durable-input key;
- a post-contention lease immediately receives any rows skipped behind locks;
- the combined leases cover all ten seeded rows exactly once; and
- persisted state reports all ten rows as leased.

A second real-server test creates one explicit read-committed transaction that
holds update row locks on five deterministic queue entries. With the production
`READPAST` query, another store claims the other five inside a one-second
command boundary. After rollback, a third store claims the held five. The
combined keys and tokens are unique and persisted state contains ten leases.

Mutation proof temporarily removed `READPAST` from the production query. The
new lock test then failed with SQL command timeout at `LeaseAsync`, exactly as
expected. Restoring `READPAST` made the test pass. The production file's final
content hash is identical to the base commit.

## Causal Timing-Test Corrections

`Registered_factories_expose_traced_addressable_component_events` now creates
its receive task before posting the event, then verifies the same trace,
correlation, address, and payload behavior.

`Fault_FailsInFlightCallers_AndFaultsCompletion` now creates its receive task
before sending, proves the input was accepted, checks the published request and
the in-flight `1` state, faults the coordinator, then proves:

- the caller receives the exact `InvalidOperationException("boom")`;
- no reply is produced;
- in-flight state returns to zero; and
- completion exposes the same exact fault.

The existing five- and thirty-second `WaitAsync` values remain bounded deadlock
guards. No timeout was increased, and no sleep, retry, skip, or global test
serialization was introduced.

## Verification Evidence

- Original archived release attempts reproduced either owner receiving zero
  leases while the other received five; both isolated reruns passed.
- The corrected multi-owner real-server test passed against the pinned server
  image digest `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
- The deterministic row-lock test passed with `READPAST` and failed by command
  timeout under the controlled removal mutation.
- Each timing test passed 8/8 focused repetitions and one concurrent execution.
- The complete Composition project passed 123/123 twice; the complete
  RequestReply project passed 27/27 twice, with zero warnings.
- The complete real-server input suite passed 90/90 with zero skips; the
  independent output suite passed 117/117 with zero skips on the same pinned
  image digest.
- Restore and the serialized Release build covered 134 projects with zero
  errors or warnings.
- The complete Release solution passed 2,509/2,509 tests across 66 projects;
  Release governance passed 141/141.
- Solution-wide formatting and direct/transitive vulnerability inspection
  passed. `git diff --check` was clean.
- Production SQL and the package-local provider README hash exactly match the
  base commit. `git diff -- src` is empty, so package preflight, archive, API,
  and binary-compatibility gates have no changed packable target in this round.
- Both provider runners removed their owned server containers and temporary
  result directories. No credential or connection string was printed.

## Review And Merge

- Implementation commit:
  `ef98b4ad59a0a1b547e4d37a99c8133521a14c97`.
- Pull request 71 was ready, clean, mergeable, and had no comments or
  actionable review findings.
- CI run `30832186883` completed `build-test` successfully on the exact
  implementation head.
- The host rejected author self-approval. No administrative or approval bypass
  was used.
- Pull request 71 merged normally as
  `da9f1d0be93b55461577c8a92aacbea589715cac`.
- Local `main` was clean and exactly equal to `origin/main` at that merge
  commit before this evidence-only closeout branch was created.

## Release Decision

This round changes only tests, documentation, memory, and the executable goal.
No production assembly or package content changes, so no package version is
advanced and no package, tag, or repository release is created or moved.
