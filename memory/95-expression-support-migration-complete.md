# Expression Support Migration Complete

Date: 2026-06-02

## Result

The shared expression-support migration is complete for current expression-based
component packages.

The only expression engine registry storage left in source is inside:

```text
FluxFlow.Components.Expressions
```

Component packages now resolve expression engines and assignable context
factories through that shared helper instead of each package owning a copy of
the same registration logic.

## Packages Covered

- `FluxFlow.Components.Expressions` `0.1.0-alpha.1`
- `FluxFlow.Components.Mapping` `0.2.0-alpha.1`
- `FluxFlow.Components.Control` `0.3.0-alpha.1`
- `FluxFlow.Components.Assertions` `0.2.0-alpha.1`
- `FluxFlow.Components.State` `0.2.0-alpha.1`
- `FluxFlow.Components.Observability` `0.2.0-alpha.1`
- `FluxFlow.Components.Routing` `0.8.0-alpha.1`

## Verification

- Local targeted tests for each migrated package.
- Full solution build and test during each package slice.
- Local package build for each released package.
- GitHub release workflow per package.
- Public-feed restore/build smoke test per package.
- Registry duplication scan:
  `rg "private readonly Dictionary<string, IFlowExpressionEngine>|private IFlowExpressionEngine\? _defaultExpressionEngine|private Func<string\?, IFlowExpressionEngine>|private readonly Dictionary<Type, .*ContextFactory>" src -g "*.cs"`

## Next

Return to component maturity work. The strongest next candidate is MQTT
reconnect and connection health behavior, but only if current consumers need it
now.
