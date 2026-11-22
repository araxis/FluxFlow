# Existing component alignment

Reviewed all eleven components in the ten existing component packages against the
explicit block/lifetime composition and synchronous callback APIs. No new component,
base class, factory layer or configuration feature was added.

## Changes

- Constructors validate options, initialize local state, construct native blocks,
  declare them to FlowLifetime, and call Observe last. Additional declarations are
  grouped with the main declarations in Gate, Router, Retry and RateLimit.
- Removed forwarding constructors in Source, Router and Retry. Source and Router
  still snapshot their input collections once. Retry still builds its policy once.
- Validation methods no longer return unchanged options solely for chained member
  access. Removed redundant capacity aliases left from the old base constructor.
  Invalid OutputCapacity diagnostics now name the actual options property. Gate
  retains its actual outputCapacity constructor parameter name.
- Retry and RateLimit allocate their owned request/reply coordinator after option
  validation and native processing-block construction. The previous field initializer
  could allocate a timer before construction rejected invalid options.
- RateLimit creates its retry-after JSON details directly from the named value,
  without an intermediate dictionary. The JSON field and error payload are unchanged.
- Corrected Batch's obsolete claim that an unlinked output never retains a value,
  Gate's old description of inherited block construction, and copied package tags
  that incorrectly classified RateLimit and Reducer as sources.

## Per-component conclusions

| Component | Retained behavior and review conclusion |
| --- | --- |
| Batch | Explicit-emission processing, size/time windows and final flush remain. Its local gate and timer coordinate real concurrent work and should stay. |
| ChangeFilter | Synchronous key selection/comparison, one remembered key and serial processing remain. |
| Deduplicate | Synchronous selection, bounded history and lazy monotonic expiry remain; no background cleanup manager is needed. |
| Mapper | The supplied ValueTask mapping function supports synchronous completion and asynchronous engines. No second options property or engine dependency is needed. |
| RateLimit | Native limiter leases cover the correlated operation; Reply remains available while business input drains. Borrowed limiter ownership is unchanged. |
| Reducer | Synchronous reduction/equality, serial state updates and unchanged-state suppression remain. |
| Retry | Native retry policy, independent attempts, deadlines and terminal Reply remain. Construction no longer needs a forwarding constructor. |
| Gate | Open and Close remain ordinary inputs; volatile state and independent input ordering remain explicit. |
| Router | All predicates are evaluated before publication. Keep the returned-result processing overload so error results retain native ordering semantics. |
| Scheduler | Separate producer start/stop, next-occurrence delegates and skipped overdue ticks remain; scheduling is not a request/reply concern. |
| Source | The snapshotted value list and single cursor remain. Each trigger consumes one value; it is not an independent producer. |

The common block setup is intentionally visible in each component. Moving it back
into a factory or base class would undo the selected authoring model. Components use
the shared processing, emission and lifetime helpers without duplicating those policies.

## Validation

Behavior, options and callable public signatures are preserved. The source-declaration
snapshot changed only for the three removed constructor initializers; declaration
counts did not change. The reviewed baseline passes after that update.

The Release solution run passed 2,135 checks with zero warnings; its sole failure was
the expected source snapshot. After updating that snapshot, the failed check passed,
bringing the verified suite to 2,136 passing checks across .NET 8 and .NET 10. New
regressions cover already-canceled Retry/RateLimit construction, completion of all
ports, event draining and continued ownership of the borrowed native limiter.

All ten isolated component-package consumers passed on both .NET 8 and .NET 10:
Batch, ChangeFilter, Deduplicate, Mapping, RateLimit, Reducer, Resilience, Routing,
Scheduler and Source. The final whitespace check is clean.
No commit or publication was performed.
