# FluxMQ Stable Migration Baseline

Date: 2026-06-02

## Result

FluxMQ migrated successfully to `FluxFlow.Engine` `1.0.0` and the rebuilt
component packages.

## Meaning

- The stable engine boundary has now been validated by the first real consumer.
- The component package rebuild solved the old binary mismatch around node
  identity types.
- The engine should now stay stable unless a real consumer issue proves that a
  boundary change is necessary.
- Future work should focus on component package maturity, package ergonomics,
  and focused adapters in consuming applications.

## Current Plan Position

The engine v1 track is closed.

The active track is component maturity:

1. Improve packages that FluxMQ now exercises heavily.
2. Keep component packages on independent prerelease tracks.
3. Avoid changing `FluxFlow.Engine` for component-only improvements.
4. Release component updates one package at a time when a package has a real
   feature or hardening slice.

## Suggested Priority

1. Routing request/response ergonomics.
2. MQTT reconnect and health behavior once a consumer needs it.
3. Shared expression-package ergonomics across mapping, control, assertions,
   routing, state, and validation.
4. Storage and session retention/query hardening when a consumer needs more
   persistence behavior.
