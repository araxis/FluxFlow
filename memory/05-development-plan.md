# Development Plan

Date: 2026-05-31

## Phase 1: package boundary cleanup

1. Remove transport-specific scenario code from the engine.
2. Remove component event constants from the engine.
3. Rename the default configuration section to `FluxFlow:Application`.
4. Decide whether dashboard metadata moves out before first public package.
5. Update README and docs to use neutral sample nodes only.
6. Add tests that prove unknown external scenario step types stay outside the base package.

## Phase 2: runtime hardening

1. Add typed-port mismatch tests.
2. Add completion propagation tests for one source, fan-out, and multiple input links.
3. Add event aggregation tests.
4. Add lifecycle tests for start, stop, fault, and dispose paths.
5. Add JSON round-trip tests for definitions and links.
6. Add cancellation tests for scenario runs and host startup.

## Phase 3: packaging

1. Keep `FluxFlow.Engine` as the base package id.
2. Multi-target `net8.0` and `net10.0`.
3. Pack README and symbols.
4. Add repository metadata to the package.
5. Decide package license before a public release.
6. Publish prerelease builds first.

## Phase 4: companion packages

1. Create component packages only after the base engine API stabilizes.
2. Start with one small package as the template.
3. Keep each component package responsible for its own validation, constants, tests, and docs.
4. Keep source application integration as a consumer, not as engine code.
