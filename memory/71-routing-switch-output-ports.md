# Routing Switch Output Ports

Date: 2026-06-02

## Decision

Make `flow.switch` more complete by adding optional route-specific output ports.

The existing `Result`, `Matched`, and `Default` ports stay unchanged. Hosts can
opt in to direct route outputs with a `routeOutputs` map when they want a
clearer graph shape.

## Scope

Added package version `0.3.0-alpha.1` with:

- `routeOutputs` on `SwitchRoutingOptions`
- dynamic output port registration for configured switch routes
- support for several route keys sharing one output port
- validation for empty route keys, invalid port names, built-in port
  collisions, and route outputs that do not match configured routes
- tests for direct route output, shared output ports, and invalid config

## Behavior

`flow.switch` still evaluates one route key per input. When the key is matched
and `routeOutputs` contains that key, the original input is sent to the mapped
output port.

If `routes` is empty, every non-empty route key is still considered matched.
If `routes` is not empty, every `routeOutputs` key must also be listed in
`routes`.

## Deferred

`flow.window` was added in `72-routing-window-component.md`.
`flow.join` was added in `73-routing-join-component.md`.
`flow.fork`, `flow.merge`, and switch route envelopes were added in
`74-routing-merge-fork-route-envelope.md`.

The remaining hardening choice is separate request and response input ports for
correlation if consumers need that graph shape.
