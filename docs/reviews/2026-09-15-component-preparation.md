# Component preparation simplification

Removed ApplicationRuntimeComponentActivator and its constructor dependency.
It had one caller, no state and no independent lifetime. Runtime preparation now
invokes registered factories directly inside its existing cleanup boundary. The
component key stays beside the preparation code; no replacement service or
interface was introduced. Local variables now refer to component instances,
rather than the obsolete descriptor terminology; two identity projections were
also removed.

Construction order, cancellation checks between factories, exception wrapping,
aggregate cleanup failures and reverse-order component disposal are retained.
Previously created instances remain registered for cleanup if a later factory
fails or preparation is canceled. The regression test now covers both cases and
checks that cancellation prevents the second factory from running, disposes the
first instance and leaves no active port generation.

This removes one internal class/file. It does not merge Core/Engine, change public
APIs or remove revision behavior. Counts remain 11 production and 29 solution
projects. Source validation is recorded under
artifacts/component-preparation-20260915; the final Engine report replaces its
initial full-regression report. Package consumers and external database services
were not rerun for this internal change.

Next: compare complete workflow construction and lifetime across direct Core and
Engine, preserving transactional updates, stable ports and resource ownership.
Avoid introducing a universal builder to share the two construction loops.
