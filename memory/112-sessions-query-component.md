# Sessions Query Component

Date: 2026-06-02

## Package

- Package: `FluxFlow.Components.Sessions`
- Version: `0.3.0-alpha.1`
- Tag: `components-sessions-v0.3.0-alpha.1`

## Goal

Add a neutral way to query session metadata before replay or dashboard
projection, without putting concrete persistence, retention policy, or app UI
contracts into the Sessions package.

## Decision

Add `session.query` as an input-driven node over the existing host-provided
session store abstraction.

The package now defines the query request/result contracts and node behavior.
The host store remains responsible for the actual persistence backend and query
implementation.

Retention and cleanup are still deferred. Query gives hosts a stable metadata
surface first, which is safer than choosing a retention contract before a real
consumer proves the shape.

## Changes

- Added `SessionQueryRequest`.
- Added `SessionQueryResult`.
- Added `SessionQueryOptions`.
- Added `ISessionStore.QuerySessionsAsync(...)`.
- Added `session.query` registration.
- Added `SessionsComponentPorts.Sessions` for optional per-session metadata
  output.
- Added query diagnostics and error codes.
- Updated the sessions sample store to implement metadata queries.
- Updated Sessions package docs and root package table.

## Runtime Shape

- Input: `SessionQueryRequest`
- Output: `SessionQueryResult`
- Sessions: `SessionMetadata`
- Errors: `FlowError`

Query supports name, name prefix, tags, started/ended ranges, active/completed
status, correlation id, and limit. Invalid requests and store failures emit
errors and later requests continue.

## Verification

- Ran the Sessions test project in Release mode.
- Ran the full solution build in Release mode.
- Ran the full solution test suite in Release mode.
- Packed `FluxFlow.Components.Sessions` `0.3.0-alpha.1`.
- Published `components-sessions-v0.3.0-alpha.1`.
- Verified a clean public-feed restore/build smoke test with the new query
  contracts.

## Result

Sessions now has a reusable metadata query primitive. This makes session
browsers, replay pickers, and host dashboards possible without moving those app
concerns into the reusable package.
