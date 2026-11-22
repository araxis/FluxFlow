# Cleanup after conditional-link filtering

Reviewed the current link parser, DI identity, condition compiler, runtime route
selection and their tests/docs after removing Parameters, Port and Fallback.

## Changes

- OneOrManyLinks now uses its collected errors to decide whether a declaration is
  valid, rather than maintaining a second boolean beside every validation branch.
  Located errors are still accumulated across entries; invalid arrays return no result.
- Scalar link parsing uses address terminology and the existing constructor default.
- FlowRouter's predicate helper is static; redundant import/spacing removed. Route
  selection, native source handling, delivery and completion behavior are unchanged.
- Delivery-policy comments explicitly say condition **exception**. A false predicate
  is a filter, not a failed delivery.
- Router tests call their sinks Unmatched or Errors rather than Fallback. This keeps
  Router's retained Unmatched output distinct from the removed link policy.
- The current validation guide no longer describes deleted revision/application
  activity APIs or an unavailable session component. Its error-link example uses
  the existing C# message.IsError expression and Address field.

No public API, configuration syntax, package dependency or runtime policy changed.
Kept native DI link identity/conflict handling, per-provider compilation reuse and
the route-selection snapshot needed to avoid partial fanout on predicate exceptions.
Historical design reports remain historical.

## Verification

Added coverage for a mixed link array reporting multiple errors at the correct
indices while a valid middle entry produces no diagnostic. Existing transactional
registration and runtime filtering/error/backpressure coverage remain.

Full Release solution test command succeeded: 1,954 executions across .NET 8/10,
including 233 Configuration tests per target and the public API baseline checks.
No build warnings/errors appeared. Diff whitespace checks pass. Log:
`artifacts/cleanup-review-20260920/post-filter-cleanup-tests.log`.
No package re-verification was needed for this internal cleanup; the previous
step verified isolated Core/Configuration consumers on both targets. No commit or
publication was performed.
