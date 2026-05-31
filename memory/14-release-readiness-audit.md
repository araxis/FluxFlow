# Release Readiness Audit

Date: 2026-05-31

## Status

The base engine is close to a prerelease package, but it is not ready for a
stable public `1.0.0` release yet.

## Ready

- Package id and repository metadata are present.
- MIT license metadata is present in the package project.
- Root `LICENSE` file is present.
- README is packed into the package.
- Symbols package generation is enabled.
- CI builds and tests on push and pull request.
- Publish workflow can publish from a tag or manual version input.
- Default package version is `0.1.0-alpha.1`.
- Dashboard/designer metadata has moved out of the base engine boundary.
- Changelog exists for the first prerelease.
- Runtime fanout, lifecycle cleanup, diagnostics, and helper-node behavior have
  focused regression tests.

## Release Gates

1. Public docs are still minimal.
   - `README.md` is usable as the package entrypoint.
   - Detailed docs under `memory/legacy-docs` are intentionally not public.
   - A prerelease can ship with the README only, but a stable release needs the
     docs rewrite.

2. Final publish check is still needed.
   - Run the full release command set immediately before tagging.

## Recommended Next Steps

1. Run build, tests, pack, diff check, and wording scan.
2. Commit all release-readiness changes.
3. Tag and publish `v0.1.0-alpha.1`.
