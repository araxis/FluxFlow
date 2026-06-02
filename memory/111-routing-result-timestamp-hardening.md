# Routing Result Timestamp Hardening

Date: 2026-06-02

## Package

- Package: `FluxFlow.Components.Routing`
- Version: `0.10.0-alpha.1`
- Tag: `components-routing-v0.10.0-alpha.1`

## Goal

Make routing result timestamps explicit so package-owned times stay under the
configured routing clock.

`FluxFlow.Components.Routing` already used `IRoutingClock` inside runtime nodes,
but several public result contracts still had hidden current-time defaults. That
made those contracts harder to test and made it possible for host-created
results to bypass the configured clock by accident.

## Decision

Routing result records should require timestamps instead of creating them
internally.

The runtime nodes remain responsible for package-owned timestamps and continue
to use `IRoutingClock`. Host-authored results must now pass the timestamp they
want to expose.

## Changes

- Made these contract properties required:
  - `FlowSwitchResult.EvaluatedAt`
  - `FlowRoute.RoutedAt`
  - `FlowMergeItem.ReceivedAt`
  - `FlowJoinResult.JoinedAt`
  - `FlowCorrelationMatch.MatchedAt`
- Removed direct current-time defaults from routing result contracts.
- Updated package metadata and release notes for `0.10.0-alpha.1`.
- Added the release entry to `CHANGELOG.md`.

## Verification

- Ran the Routing test project in Release mode.
- Ran the full solution build in Release mode.
- Ran the full solution test suite in Release mode.
- Packed `FluxFlow.Components.Routing` `0.10.0-alpha.1`.
- Scanned routing sources and confirmed direct current-time usage remains only
  in `SystemRoutingClock` and node code that calls the configured clock.
- Published `components-routing-v0.10.0-alpha.1`.
- Verified a clean public-feed restore/build smoke test with
  `FluxFlow.Components.Routing` `0.10.0-alpha.1`.

## Result

Routing now has deterministic package-owned timing all the way through the
public result contracts. This closes the last clock-hardening gap found in the
Routing package during this pass.
