# Routing Correlation Split Inputs

Date: 2026-06-02

## Decision

Harden `flow.correlation` by supporting two graph shapes:

- single-stream mode: `Input` plus `sideExpression`
- split-stream mode: `Request` and `Response` inputs without `sideExpression`

This keeps the existing component behavior while making request/response
workflows easier to compose when the two sides already come from different
streams.

## Scope

Prepared `FluxFlow.Components.Routing` `0.7.0-alpha.1` with:

- `RoutingComponentPorts.Request`
- `RoutingComponentPorts.Response`
- optional `CorrelationRoutingOptions.SideExpression`
- conditional input registration so a node exposes either `Input` or
  `Request`/`Response`
- unchanged `Matched`, `Timeouts`, and `Errors` outputs
- tests for split input matching, split input completion timeouts, and existing
  single-stream behavior

## Behavior

When `sideExpression` is configured, `flow.correlation` exposes only `Input`.
Each input item evaluates `keyExpression` and `sideExpression`, then pairs by
configured request and response side names.

When `sideExpression` is omitted, `flow.correlation` exposes `Request` and
`Response`. Each port supplies its side implicitly, so the item only needs to
evaluate `keyExpression`.

Only the active input shape is registered. This avoids unlinked optional ports
keeping the node alive.

## Verification

- `dotnet test tests\FluxFlow.Components.Routing.Tests\FluxFlow.Components.Routing.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- Released `FluxFlow.Components.Routing` `0.7.0-alpha.1`.
- Verified fresh public-feed restore/build with
  `FluxFlow.Components.Routing` `0.7.0-alpha.1`.

## Next

Continue component maturity work while keeping the engine boundary stable.
