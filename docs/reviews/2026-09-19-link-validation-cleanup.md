# Link validation implementation cleanup

Reviewed the ordinary DocumentLinks path and revision ApplicationLinkCompiler.
They already share ApplicationLinkDeclarationParser and ApplicationConditionCompiler.
They cannot be replaced by one consumer without a behavior decision: ordinary
document construction merges identical declarations and rejects conflicting policies,
while the revision compiler diagnoses repeated source/target pairs as DuplicateLink.
The latter also accumulates diagnostics and preserves declaration projections.
These differences were retained; this step does not claim complete pipeline unification.

## Completed

- ApplicationConditionCompiler uses one session-local dictionary of Lazy compiled
  expressions. .NET retains either the compiled expression or its original failure.
  Removed the second exception dictionary, explicit failure lookup/rethrow and
  success/failure bookkeeping. Conditions are still compiled when requested during
  linking; message evaluation is not deferred or cached by Lazy.
- Removed the private ApplicationAddressComparer. ApplicationAddress already
  implements IEquatable and matching value-based GetHashCode; standard Dictionary
  and GroupBy equality provide the same behavior for system ports and exclusive claims.
- No new class, interface, public API or manager. Two production files decreased
  from 881 to 864 lines (17 lines), including comments/blank lines. The useful change
  is fewer custom mechanisms, not a new authoring model or a performance claim.

The cache remains local to a compilation session and is not a concurrency API.
Keys retain engine, expression, input type and bound parameters. Scoped engine
selection, parameter isolation, diagnostic origin/order and original error identity
are unchanged. The existing mixed-origin regression now also compiles a second
session, proving successes and failures are not retained between compilations.

## Verification

- Release solution build: zero warnings/errors.
- Full run: 2,524 passed and one source-baseline mismatch, zero skipped, 29 reports.
  The baseline scanner includes three public members of the removed private nested
  comparer; no exported API was removed. Reviewed and updated that source baseline,
  then reran its test normally: passed. All 2,525 unique cases are now verified;
  the original failed report remains available rather than being overwritten.
- Existing compiler, JSON, parameter, scoped-engine, routing and revision tests pass
  on their configured targets. No additional package qualification was run for this
  internal-only change; the preceding address-link package result is historical.
- Evidence: artifacts/link-validation-cleanup/ (build.log, tests.log, tests/,
  api-update.log, api-check.log, api-tests/ and verification.json).

No commit, push or publication. Further unification of duplicate-link validation
requires choosing the intended behavior explicitly; do not silently change it as
part of a structural refactor.
