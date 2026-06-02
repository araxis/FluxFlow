# Engine Beta Release Prep

Date: 2026-06-02

## Target

Prepare `FluxFlow.Engine` `0.6.0-beta.1`.

This beta is the stabilization release after the public API audit. It packages
the engine-only cleanup needed before the first consumer moves from the alpha
line to the beta line.

## Included Changes

- Move `FlowNodeId` into `FluxFlow.Engine.Components`.
- Remove concrete expression-language adapters from the engine package.
- Remove expression parser dependencies from the engine package.
- Keep `IFlowExpressionEngine`, predicates, mapper helper contracts, and
  conditional-link runtime support in the engine.
- Require a host-provided expression engine when executable definitions use
  link `when` conditions.
- Add `ApplicationRuntimeBuildErrorCode.MissingExpressionEngine`.
- Add `FlowApplicationHost.Create(...)` overloads for host-supplied link
  condition expression engines.
- Update the sample app to provide its own small expression engine.

## Release Inputs

- Project version: `0.6.0-beta.1`.
- Release tag: `engine-v0.6.0-beta.1`.
- Changelog section: `FluxFlow.Engine 0.6.0-beta.1`.

## Verification Plan

- Full solution build: passed.
- Full solution tests: passed.
- Sample app run: passed.
- Release-note extraction: passed.
- Local package pack: passed.
- Local package install smoke test: passed.
- Branch CI after commit.
- Release workflow after tag.

The package smoke restored `FluxFlow.Engine` `0.6.0-beta.1` from the local
package output and compiled a minimal consumer against:

- `ApplicationDefinition`
- `RuntimeNodeFactoryRegistry`
- `FlowNodeId` from `FluxFlow.Engine.Components`

## Next Step

Run the beta gates, commit the release-prep changes, push `main`, then publish
by pushing `engine-v0.6.0-beta.1` once branch CI is green.
