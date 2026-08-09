# Code-First Prerelease Plan

The code-first simplification is released first as a coordinated prerelease.
This protects the stable package line while consumers validate the breaking
authoring changes through real package restore and execution.

## Scope

Exactly 31 packages move. Twenty-seven contain direct candidate changes and four
are dependency-only releases: both durable input providers, both durable output
providers, and Fluent Hosting move with their new parent major lines. The other
29 manifest packages remain at their existing published versions.

The exact versions and full execution contract are recorded in
`goals/2026-08-08-code-first-prerelease-release/README.md`.

## Waves

1. Composition 7.0.0-rc.1.
2. Designer 6.0.0-rc.1 and Engine 8.0.0-rc.1.
3. Nineteen component-composition packages, both durable cores, Engine health
   checks, and Fluent.
4. Four durability providers and Fluent Hosting.

Each wave waits for public indexing, isolated restore/load, and the exact
repository release before the next wave begins.

## Publication identity

Every tag is package-specific and targets one immutable merged commit. The
workflow verifies the declared published baseline, archive, package-only
consumer, exact public absence, upload, public-feed restore, and repository
release in that order.

Publishing uses the `release` environment and trusted short-lived credentials.
The feed-side policy must match:

- repository owner: `araxis`;
- repository: `FluxFlow`;
- workflow file: `publish-nuget.yml`; and
- environment: `release`.

The environment secret `NUGET_USER` contains the package-feed profile name, not
an email address. Keep the old repository API-key secret until one trusted
publication has completed, but the workflow no longer consumes it.

## Failure boundary

Never treat a network or protocol failure as package absence. Never skip a
duplicate, move a successful tag, or republish a successful version.

Before rerunning a failure that occurred before upload, prove both the exact
package and repository release are absent. After a successful upload, resume
only incomplete indexing, verification, or release-record work using the
retained artifacts.

## External acceptance

After all 31 prereleases are indexed, the separate package-only pilot restores
from the public feed and proves code-first execution, health, portable JSON
rollback, retained routing, and two-process SQL-file durability. Stable
promotion begins only after that public-feed proof is green and recorded.

## Release status

The prerelease completed from immutable commit
`d6c245df82fb2958a77cff04985811fb49f12b04` after pull request `#77` and main CI
run `31295479215` passed. Trusted short-lived publication succeeded for all 31
packages in the four dependency waves. Every exact version is publicly indexed
and has a matching prerelease record with package and symbol assets targeting
that commit.

The separate pilot at commit `9e5699b` then restored the exact acceptance graph
from the public feed only. Its build completed with zero warnings and errors,
all 6 tests passed, code-first/health/JSON/durability execution succeeded, and
the default runner removed its owned cache and restart data.

The release-candidate acceptance boundary is therefore complete. Stable
promotion remains a separate immutable release after the observation period.
