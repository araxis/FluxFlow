# Designer Resource Picker Hint Contracts

Date: 2026-07-02

## Summary

Added neutral Designer package contracts for reading host-owned resource picker
hints from existing component design metadata. This is a host-facing helper
layer only: component metadata content, renderer behavior, resource catalogs,
keyed resource resolution, resource lifetimes, runtime behavior, hot reload,
and package-family metadata providers are unchanged.

## Changes

- Added `ComponentResourcePickerHint` as an immutable host-facing view over a
  component resource picker hint.
- Added `ComponentResourcePickerHints.Create(...)` overloads for one
  `ComponentDesignMetadata` item and for a `ComponentDesignMetadataCatalog`.
- The helper includes only resources marked `ownership=host-owned` with a
  non-empty `pickerKind`, preserves resource order within each component, sorts
  catalog output by component type for deterministic host enumeration, and
  parses comma-separated `requiredWhenAnyOption` values into typed option names.
- Bumped `FluxFlow.Components.Designer` from `2.16.0` to `2.17.0` because this
  is additive public API.
- Updated the Designer README, public API overview, coverage matrix, changelog,
  and public API baseline for the new contract surface.

## Verification

- Focused Designer tests cover metadata and catalog picker hint extraction,
  filtering, ordering, display/type fields, key patterns, related options, and
  parsed conditional option names.
- Designer tests passed (`97`).
- Release tests passed (`92`), including the intentional public API baseline
  update for the additive Designer contract surface.
- Controlled Release and Debug solution builds passed with 0 warnings and 0
  errors.
- `components-designer` `2.17.0` passed binary compatibility preflight against
  published baseline `2.16.0`, package release preflight, and fast release
  dry-run with package archive, consumer smoke, and feed verification.

## Boundaries

Hosts still own resource catalogs, keyed registrations, secrets, lifetimes,
rendering, localization, and disposal. The new helper does not resolve,
validate, create, enumerate, or dispose resources.
