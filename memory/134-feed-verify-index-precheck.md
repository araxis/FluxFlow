# Feed Verify Index Pre-Check

Date: 2026-06-12

## Problem

The post-publish `Verify package feed` step (`eng/package-feed-verify.ps1`)
retried a full `dotnet restore` 20 times at 15s (a 5-minute window). During
the review-remediation release three runs (Expressions, State, Expectations)
pushed successfully but exhausted that window waiting for nuget.org to index
the new version, each needing a manual rerun.

## Fix

Add a cheap flat-container availability poll before the restore-based check.
The flat-container `index.json` is exactly what `dotnet restore` reads for the
version list, and a GET is far cheaper than a restore, so polling it on a
generous window absorbs indexing lag without burning restore attempts; the
subsequent restore then passes on the first try.

- `Resolve-FlatContainerBase` reads the source's service index and finds the
  `PackageBaseAddress/3.0.0` resource, so it works for any v3 feed, not just
  nuget.org.
- `Wait-PackageIndexed` polls `{base}{id-lower}/index.json` for the version,
  treating 404s and transient errors as not-yet.
- New params `IndexAttempts` (default 40) and `IndexDelaySeconds` (default
  15); `IndexAttempts = 0` disables the pre-check. The pre-check is skipped
  for local directory sources (so the local dry run is unaffected) and runs
  only after the `-PrepareOnly` early return (so release guard tests are
  unaffected).
- `publish-nuget.yml` passes `-IndexAttempts 60 -IndexDelaySeconds 15` (a
  15-minute cap) and keeps the existing restore retry as a residual safety net.

## Verification

- `package-feed-verify.ps1` parses clean; release guard tests pass at 32
  (added `Feed_verify_script_rejects_negative_index_attempts`).
- Smoke run against the published `FluxFlow.Engine 1.1.0`:
  `FLAT_CONTAINER_BASE=...` resolved, `INDEX_OK` on attempt 1, then
  `VERIFY_ATTEMPT=1` and `FEED_OK` — the restore passed on the first try.
