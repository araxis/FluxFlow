# Component Package Template Sample

Date: 2026-06-01

## Goal

Add a small buildable package-authoring template so new component families can
copy a consistent shape.

## Decisions

- Sample package project: `samples/FluxFlow.ComponentPackageTemplate`.
- Test project: `tests/FluxFlow.ComponentPackageTemplate.Tests`.
- Node type: `template.enrich`.
- Ports:
  - `Input`
  - `Output`
- Contracts:
  - `TemplateInput`
  - `TemplateOutput`
- Options:
  - `TemplateComponentOptions`
  - `TemplateEnrichOptions`
- The sample includes diagnostics, error codes, options parsing, module
  registration, an extension method, and deterministic tests.
- The sample package is not packable by default.

## Status

Implemented as a copyable class-library sample plus focused tests.
