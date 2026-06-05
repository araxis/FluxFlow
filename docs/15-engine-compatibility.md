# Engine Compatibility

This page describes the compatibility promise for `FluxFlow.Engine` after
`1.0.0`.

Component packages release independently. Their compatibility is described by
their own package version.

## Stable Surface

The stable engine surface includes:

- public types in `FluxFlow.Engine`
- public types in `FluxFlow.Engine.Components`
- public types in `FluxFlow.Engine.Definitions`
- public types in `FluxFlow.Engine.Mapping`
- public types in `FluxFlow.Engine.Runtime`
- executable definition JSON shape
- validation and runtime build error codes
- runtime lifecycle states
- event, diagnostic, and state stream contracts
- package registration helper contracts

The engine does not guarantee internal implementation details such as queue
layout, collector internals, fanout pump internals, cleanup helpers, or private
node wiring.

## Patch Releases

Patch releases keep source and binary compatibility for normal consumers.

Patch releases may include:

- bug fixes
- test hardening
- documentation fixes
- clearer error messages
- stricter validation only when the previous behavior was invalid or unsafe
- performance improvements that preserve behavior

Patch releases should not rename public types, remove members, change persisted
definition shape, or require new host services.

## Minor Releases

Minor releases may add public API while preserving existing behavior.

Minor releases may include:

- new optional helper types
- new optional validation error codes
- new optional diagnostic attributes
- new overloads
- new definition fields with safe defaults
- additive lifecycle or observation features

Minor releases should avoid changing defaults that existing definitions depend
on.

## Major Releases

Major releases are for breaking changes.

Major changes include:

- renaming or removing public types or members
- changing required definition fields
- changing JSON shape in a way that old definitions no longer load
- changing runtime build/start/stop semantics
- changing error-code meaning
- changing port linking rules in a way that alters successful existing graphs
- adding required host dependencies for existing features

## Definition Compatibility

Applications should treat `ApplicationDefinition` as an executable DTO, not as
their full product workspace model.

Recommended host pattern:

1. Keep app-specific workspace files under host ownership.
2. Project executable resources and workflows into `ApplicationDefinition`.
3. Validate the host workspace before projection.
4. Validate the projected definition through `ApplicationDefinitionValidator`.
5. Build with `ApplicationRuntimeBuilder` or `FlowApplicationHost`.

This keeps future engine changes focused on executable workflow behavior and
keeps application-specific schema migrations outside the engine package.

## Expression Compatibility

`FluxFlow.Engine` owns `IFlowExpressionEngine` and expression predicate
contracts. It does not own concrete expression languages.

Applications that persist link `when` expressions own:

- the expression language
- expression validation
- available variables
- expression migration
- any expression package dependencies

The engine guarantees that default link condition context exposes the current
item as `input` and `value` when `ExpressionFlowPredicate<TInput>` is used.

## Component Package Compatibility

Component packages release independently from the engine and now have their own
stable `1.0.0` line. A component package update does not require an engine
update unless the component truly needs a new engine contract.

Recommended host rule:

- pin the engine package intentionally
- pin each component package independently
- update one package family at a time when possible
- run host workflow tests after every package update

Next: [Migration 0.5 To 0.6](16-migration-0.5-to-0.6.md)
