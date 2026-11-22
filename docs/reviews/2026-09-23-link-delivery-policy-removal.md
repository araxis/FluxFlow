# Remove delivery policy from links

The user explicitly deferred general delivery/output policy design and removed it
from the current link contract. This supersedes earlier preservation requirements
for per-link Reliable, RejectWhenFull and IsolateRejection.

## Final behavior

Links select destinations with optional predicates and use native SendAsync to wait
for target capacity. A false predicate filters its link; no matches consumes the
message without routing failure. Predicate exceptions and permanently refused
admission fault execution, preserving the previous default behavior. Acceptance is
not processing completion, persistence, retry or acknowledgement.

## Removed

- FlowDeliveryPolicy and FlowLink.Delivery/OnRejected/ReportRejection.
- Delivery/rejection-handler arguments from FlowExtensions.Link.
- LinkConfiguration.Delivery and its parser branch. Even the former default
  Delivery: Reliable is rejected as an unknown field, with atomic registration.
- The policy tuple/conflict branch in DI link registration. A native keyed delegate
  is now identified directly by canonical source/target, condition and engine;
  the unkeyed alias remains the single execution path.
- Immediate admission and per-link rejection callback branches in FlowOutput.
  Its private RoutePolicy record is gone; route selection and located rejection text
  are stored as a pair on the existing native link.
- Obsolete policy-specific tests. Retained rejection, condition failure, bounded
  capacity, cancellation, native reservations, fanout and lifecycle tests use the
  ordinary Link API. RouteIsolationTests is renamed RouteFailureTests.

No replacement policy class, component option or package was added. Existing
component event isolation and public native Dataflow source behavior are unchanged.
Core/Configuration guides and the public API baseline are updated. Consumers must
remove old fields/arguments and rebuild against the changed API.

## Verification

Core: 244 tests per target; Configuration: 233 tests per target (.NET 8/10).
Full Release solution run passed 1,900 test executions, with no build warnings/errors.
The lower count reflects removed policy cases; retained default-path scenarios still
cover both standard and native sources. After removing unused test probe hooks, the
final Core rerun passed 244 per target again. Public API baseline and diff checks pass.
Isolated Core and Configuration NuGet consumers pass on .NET 8.0.31 and .NET 10.0.12.
Logs are under `artifacts/cleanup-review-20260920/link-policy-*.log`.
No commit or publication.
