# Conditional links as filters

The user chose the existing Workflow Engine ConditionBlock behavior for links:
false consumes the message without forwarding it. This replaces the former contract
that required fallback or faulted execution when no route matched.

## Result

- Every matching link receives the original message; an unconditional link always matches.
- No matches completes that delivery without a routing error, retry or retained waiter.
  Registered outputs with no links drain as well. Native blocks can still retain their
  own internal data according to their implementation; no claim of immediate GC is made.
- Removed Fallback from LinkConfiguration and parsing/identity, LinkFallback from
  FlowExtensions, IsFallback from FlowLink, duplicate-fallback validation in Application,
  and the entire FlowRouteSelection helper. FlowRouter now directly evaluates predicates.
- Real predicate exceptions still abort strict selection with no partially selected
  business delivery. IsolateRejection still reports its errors; admission, backpressure,
  fanout and component lifecycle failure policies are unchanged.
- The Router component's Unmatched output is independent and remains unchanged.
- The test-only TestSink was used solely to drain outputs that previously faulted when
  unmatched. Removed it, its calls, and test/benchmark project references.

## Migration

Old Fallback fields now fail located, atomic registration, including Fallback: false.
Remove them. If a separate branch is actually needed, define an explicit predicate
for that branch. Do not replace an old fallback with an unconditional link unless
that branch should receive every message. There is no new discard setting or manager.
Rebuild callers for LinkConfiguration's changed constructor and removed public APIs.

## Verification

- Core: 271 passing cases per .NET 8/10 target. Tests exercise all-false and no-link
  outputs on native and standard sources, link capacity one, normal completion,
  repeated envelope evaluation, later matching messages, fanout and real failures.
- Configuration: 232 passing cases per target. Real C#/JSONata expressions reject an
  early message normally and still deliver a later match; both declaration directions,
  compile reuse and duplicate detection remain covered. Old fallback syntax is rejected.
- All solution test projects passed: 1,952 executions. That command also exposed an
  obsolete benchmark Compile reference to TestSink; after removing it, the full Release
  solution build passed with zero warnings/errors. No test behavior was changed afterward.
- Public API baseline updated and verified. Isolated Core and Configuration packages
  passed .NET 8.0.31 and .NET 10.0.12. Diff whitespace checks passed.

The first new native-source drain case exceeded its test fixture's fixed Post capacity;
it was corrected to three messages (still exceeding the link's capacity of one), then
both complete Core target suites passed. No production change was made for that fixture.

Logs are in artifacts/cleanup-review-20260920/link-filter-*.log.
No commit, push, merge or package publication.
