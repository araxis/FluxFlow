# vNext Resource Address And Ownership Alignment

Date: 2026-07-20

## Status

The thirty-first bounded vNext milestone is implemented on local branch
`work/resource-infrastructure-vnext`. No push, tag, publication, pull request,
or merge was performed.

This milestone aligns Resources, Secrets, and Configuration with the canonical
application address and immutable provider-snapshot model already owned by
Composition and Hosting.

## Canonical Addresses

- `ResourceName` and `SecretName` now require canonical nested resource
  addresses such as `Resources.Infrastructure.Primary` and
  `Resources.Security.BrokerCredentials`.
- Both wrappers accept `ApplicationAddress`, expose the parsed address, and
  reject workflow, port, system, flat, empty, or whitespace-normalized names.
- Resource and secret catalogs preserve ordinal, case-sensitive canonical
  address values. Configuration request builders accept `ApplicationAddress`
  directly and retain typed `ResourceName` and `SecretName` overloads.
- Resources, Secrets, and Configuration now depend narrowly on Composition for
  the shared address model. Release boundary tests allow only these three
  support packages to take that dependency and continue rejecting Engine,
  Hosting, Nodes, and Designer coupling.

## Ownership Boundary

- `ResourceOwnership` distinguishes `Host`, `ResourceRevision`, and `External`.
  Resource and secret descriptors require one explicit value, and diagnostics
  reject missing or undefined ownership.
- Factory-based keyed registrations are created and disposed by their service
  provider. External bridges use explicit `AddExternal...` methods and direct
  instance registrations, so provider disposal does not capture external
  ownership.
- An `IResourceLookup` is exposed as descriptor metadata through a private
  non-owning view. This prevents the same disposable lookup from being tracked
  twice under two keyed service contracts.
- The in-memory catalog and secret builders require ownership at declaration
  time; no implicit ownership fallback remains.

## Versions And API

- `FluxFlow.Components.Resources` moved from `1.6.1` to `2.0.0`.
- `FluxFlow.Components.Secrets` moved from `1.6.1` to `2.0.0`.
- `FluxFlow.Components.Configuration` moved from `1.5.1` to `2.0.0`.
- Public API baseline entries 44 through 46 changed for canonical address
  overloads, explicit ownership, and explicit external registration methods.
- SDK package validation against the preceding 1.x packages reports only the
  planned removals of flat string APIs and implicitly owned registrations.
  These are intentional major-version breaks; no suppression was added.

## Verification

- Resources tests: 56 passed, 0 warnings.
- Secrets tests: 88 passed, 0 warnings.
- Configuration tests: 40 passed, 0 warnings.
- Release tests: 94 passed, 0 warnings.
- Complete Release no-build sweep: 2,158 tests across 63 projects passed with
  0 warnings.
- Controlled Debug and Release solution builds passed with no errors.
- Release preflight passed for all three `2.0.0` packages.
- Isolated local-source package dry-runs passed for Resources, Secrets, and
  Configuration, including archive, restore/build, and feed-style checks.
- A package-only net8 consumer validated canonical addresses and ownership,
  then proved provider-created disposal occurs exactly once while an external
  lookup remains undisposed.

## Next Boundary

The ordinary component-family migration and resource/configuration alignment
are complete. The next bounded milestone is final Hosting integration: load a
canonical application into immutable resource/workflow provider snapshots,
activate revisions through stable ports, and expose the host-owned application
lifecycle without introducing scanning, fallback providers, or a second
registration framework. Designer persistence follows as a separate milestone.
