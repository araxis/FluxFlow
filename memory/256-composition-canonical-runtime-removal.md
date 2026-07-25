# Composition Canonical Runtime Removal

Date: 2026-07-25

## Status

The Composition and Composition Hosting canonical-runtime removal is complete
on local branch `work/canonical-vnext-cleanup`. No push, tag, package
publication, pull request, or merge was performed.

## Canonical Boundary

- `ApplicationDefinition`, `ComponentDefinition`, canonical addresses, compiled
  links, revision hosting, and component-oriented factory contexts are the only
  maintained application runtime model.
- `CompositionNodeFactoryContext` now exposes canonical component identity and
  flat options/resources only. Runtime node keys and runtime node descriptors
  use component terminology.
- Canonical shared-input fan-in remains revision-owned: a target stays active
  for all sources, source faults are observed once during drain, and runtime
  cleanup attempts every link and component before aggregating cleanup errors.
- `FluxFlow.Composition.Hosting` retains canonical application revision sources,
  coordinators, service-provider snapshots, and explicit registry contributors.

## Explicit Migration Boundary

- Added `LegacyCompositionDefinitionMigrator` as the one explicit conversion
  boundary for retired `Workflows`/`Nodes`/`Links` documents.
- JSON, UTF-8, and `IConfiguration` entry points produce canonical
  `ApplicationDefinition` data with flat component properties, canonical local
  or cross-workflow addresses, and scalar-or-array fan-in links.
- Migration rejects property collisions and malformed legacy declarations
  rather than retaining a second executable runtime path.
- `docs/22-canonical-vnext-migration.md` records old-to-new JSON and C# hosting
  replacements. Current READMEs and samples use the canonical runtime only.

## Removed Compatibility

- Removed `CompositionDefinition`, its nested DTOs, JSON helpers, builder,
  configuration loader, validator, reload planner contracts, runtime builder,
  build result, and legacy input-completion link.
- Removed node-oriented factory context members and legacy runtime-node
  terminology where canonical component equivalents exist.
- Removed the Hosting composition runtime host, hosted service, builder,
  options, exception, static/configuration definition sources, legacy DI
  extensions, and compatibility resource extensions.
- Removed tests that exercised the retired runtime and replaced required
  migration, fan-in, source-fault, and cleanup behavior with canonical tests.

## Versioning And API Review

- `FluxFlow.Composition`: `2.7.0` to `3.0.0`.
- `FluxFlow.Composition.Hosting`: `2.3.0` to `3.0.0`.
- Public source-declaration baseline index 2 changes from 369 to 290
  declarations; index 3 changes from 180 to 136 declarations.
- SDK package validation against the preceding published versions reports the
  intentional CP0001/CP0002 removals on net8.0 and net10.0. The reports match
  the reviewed ledger and no compatibility suppressions were added.
- Both standard release preflights pass with their `3.0.0` metadata and
  changelog entries.

## Verification

- Composition: 107 passed, zero warnings.
- Composition Hosting: 29 passed, zero warnings.
- Engine: 111 passed, zero warnings.
- Fluent: 21 passed; Fluent Hosting: 5 passed.
- Designer: 112 passed; Designer Host: 22 passed.
- Components Configuration: 40 passed.
- Release: 96 passed, zero warnings.
- Controlled Debug and Release solution builds each completed 131 projects with
  zero warnings and zero errors after shutting down one verified stale
  FluxFlow-owned build parent.
- A fresh temporary source outside the repository contained all 58 current
  packages. Composition and Hosting archive checks, smoke consumers, feed
  checks, and release dry-runs passed against that source.
- A temporary net8.0 consumer with 58 direct current-package references restored
  from the complete temporary source and built in Release with zero warnings
  and zero errors.

## Remaining Program Work

The Composition and Hosting ledger entries are `removed-after-parity`. The next
bounded phase is Engine assembler/port simplification, followed by structural
Control/Routing removal, remaining family audits including MQTT, and the final
whole-program documentation and compatibility audit.
