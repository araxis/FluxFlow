# Component 1.0 Readiness

Date: 2026-06-04

## Decision

Promote component packages to `1.0.0` in dependency waves using targeted
readiness fixes only. The engine stays on its existing stable boundary.

No new component families are added in this track. Public component contracts are
treated as frozen once a package passes its wave gates.

## Readiness Matrix

| Wave | Package | Pre-v1 version | Dependencies | Status | Blockers |
| --- | --- | --- | --- | --- | --- |
| 1 | `FluxFlow.Components.Resources` | `0.1.0-alpha.1` | none | Local gates passed | release pending |
| 1 | `FluxFlow.Components.Secrets` | `0.2.0-alpha.1` | none | Local gates passed | release pending |
| 1 | `FluxFlow.Components.Configuration` | `0.1.0-alpha.1` | Resources, Secrets | Local gates passed | release pending |
| 1 | `FluxFlow.Components.Designer` | `0.1.0-alpha.1` | Engine | Local gates passed | release pending |
| 1 | `FluxFlow.Components.Expressions` | `0.1.0-alpha.1` | Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Serialization` | `0.1.1-alpha.1` | Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Payloads` | `0.1.1-alpha.1` | Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Validation` | `0.2.0-alpha.1` | Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Mapping` | `0.2.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Control` | `0.3.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Assertions` | `0.2.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Sources` | `0.2.0-alpha.1` | Engine | Local gates passed | release pending |
| 2 | `FluxFlow.Components.Timers` | `0.5.0-alpha.1` | Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Routing` | `0.10.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Metrics` | `0.2.0-alpha.1` | Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Observability` | `0.3.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Projections` | `0.1.0-alpha.1` | Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Expectations` | `0.1.0-alpha.1` | Projections, Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Journal` | `0.1.0-alpha.1` | Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.State` | `0.3.0-alpha.1` | Expressions, Engine | Local gates passed | release pending |
| 3 | `FluxFlow.Components.Sessions` | `0.3.0-alpha.1` | Engine | Local gates passed | release pending |
| 4 | `FluxFlow.Components.Storage` | `0.4.0-alpha.1` | Engine | Local gates passed | release pending |
| 4 | `FluxFlow.Components.Storage.FileSystem` | `0.3.0-alpha.1` | Storage | Local gates passed | release pending |
| 4 | `FluxFlow.Components.Storage.SqlFile` | `0.3.0-alpha.1` | Storage | Local gates passed | release pending |
| 4 | `FluxFlow.Components.FileSystem` | `0.5.0-alpha.1` | Engine | Local gates passed | release pending |
| 4 | `FluxFlow.Components.Http` | `0.2.0-alpha.1` | Engine | Local gates passed | release pending |
| 4 | `FluxFlow.Components.Mqtt` | `0.5.0-alpha.1` | Engine | Local gates passed | release pending |

## Gates

- Package project version is `1.0.0`.
- Package release notes describe the stable component boundary.
- Changelog contains a `1.0.0` section for each package id.
- Package README remains present and packed.
- Focused package tests pass.
- Full solution build and tests pass.
- Package preflight and dry run pass before tag creation.
- Fresh package feed verification passes after each release.

## Notes

The readiness pass found no repo-local blocker requiring a broad public API
redesign. Any release-gate failure should be treated as a targeted v1 blocker
and fixed before creating the affected package tag.
