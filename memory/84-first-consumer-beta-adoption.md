# First Consumer Beta Adoption

Date: 2026-06-02

## Result

The first consumer migrated to `FluxFlow.Engine` `0.6.0-beta.1` successfully.

No engine compatibility issue was reported during the migration.

## What This Proves

- The `FlowNodeId` namespace move is manageable for a real consumer.
- The host-provided expression engine boundary works for existing conditional
  link workflows.
- Removing concrete expression adapters from the engine package did not block
  consumer migration.
- The executable definition and runtime lifecycle boundary stayed usable after
  the beta cleanup.

## Decision

Proceed with the `FluxFlow.Engine` `1.0.0` release prep.

The component packages can remain prerelease. The stable engine release does
not imply that every component package is stable.

## Next Step

Complete the v1 documentation gates, run the release gates again, and publish
`FluxFlow.Engine` `1.0.0` if verification remains clean.
