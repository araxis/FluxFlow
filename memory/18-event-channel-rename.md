# Event Channel Rename

Date: 2026-05-31

## Decision

Use `Channel` as the first-class route/group label on `FlowEvent`.

## Reasoning

`Topic` was readable for publish/subscribe style event streams, but it also
suggested a specific transport vocabulary. The engine should stay neutral, so
the generic event metadata is now named `Channel`.

Keeping this as a property is still useful:

- Dashboard and logging adapters can group events without parsing attributes.
- Component packages can map their own domain-specific route names into this
  neutral field.
- Applications still have `Attributes` for details that are not common enough
  to belong on the core event contract.

## API Change

- `FlowEvent.Topic` becomes `FlowEvent.Channel`.
- `EventFlowNodeBase.EmitEvent(... topic ...)` becomes
  `EventFlowNodeBase.EmitEvent(... channel ...)`.

This is a prerelease breaking change released in `0.3.0-alpha.1`.

## Verification

- Release build, tests, package creation, and post-feature review passed.
- Compiled API check found `Channel` and no old route metadata property.
- Release automation completed in run `26713377988`.
- Public package feed listed `0.3.0-alpha.1`.
- Fresh package install from the public feed succeeded after clearing stale
  local HTTP cache.
