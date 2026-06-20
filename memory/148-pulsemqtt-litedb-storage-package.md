# 148 - Pulse MQTT LiteDB storage package

Date: 2026-06-20

Status: merged, tagged, released, and indexed upstream. The follow-up
development preview is also published.

## Decision

Add `Pulse.Mqtt.Storage.LiteDB` as a second durable storage provider beside
`Pulse.Mqtt.Storage.Sqlite`.

The package exposes the same storage boundary shape as SQLite:

- `LiteDbMessageStore` implements `IMessageStore` for the offline publish queue.
- `LiteDbSessionStore` implements `ISessionStore` for subscriptions and
  in-flight QoS state.
- `LiteDbStorageException` reports provider-specific open/decode failures.

The implementation stores LiteDB rows as `BsonDocument` rather than mapped POCOs,
so provider mapping/reflection stays out of the Pulse storage behavior. Publish
packets are persisted as MQTT wire blobs using the existing packet codec pattern,
matching SQLite's fidelity for MQTT 5 fields. A shared internal `LiteDbStore`
owns the database handle and serializes operations through an async gate so the
queue count, overflow policy, and blocking-capacity semaphore stay authoritative.

The package uses `LiteDB` `5.0.21`, targets `net8.0` and `net10.0`, is included
in the solution, release workflow package list, README package table, docs
package reference, resilience guide, migration guide, and changelog.

## Verification

In `D:\Projects\MqttNg`:

- `dotnet build src\Pulse.Mqtt.Storage.LiteDB\Pulse.Mqtt.Storage.LiteDB.csproj --configuration Release --nologo` passed.
- `dotnet test tests\Pulse.Mqtt.Storage.LiteDB.Tests\Pulse.Mqtt.Storage.LiteDB.Tests.csproj --configuration Release --verbosity quiet --nologo` passed:
  21 tests, 0 failed, 0 skipped.
- `dotnet build -c Release --nologo` passed with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build --logger "console;verbosity=minimal" --filter "Category!=BrokerMatrix&Category!=Soak" --nologo` passed:
  463 tests, 0 failed, 0 skipped.
- `dotnet pack -c Release --no-build -o artifacts\packages --nologo` produced
  ten packages, including `Pulse.Mqtt.Storage.LiteDB.2.3.0.nupkg`.
- `npm run docs:build` passed from `D:\Projects\MqttNg\docs`.
- `git diff --check` reported only existing line-ending warnings.

## Release

- PR #101 (`https://github.com/araxis/pulse-mqtt/pull/101`) merged the LiteDB
  storage add-on with squash merge commit
  `a12ed54e4093ca06b478f957d9d514abb185f087`.
- Tag `v2.3.0` was pushed on that merge. Release workflow run `27876350812`
  passed and created
  `https://github.com/araxis/pulse-mqtt/releases/tag/v2.3.0`.
- NuGet flat-container checks confirmed all ten stable `2.3.0` packages were
  indexed, including `Pulse.Mqtt.Storage.LiteDB`.
- PR #102 (`https://github.com/araxis/pulse-mqtt/pull/102`) opened the next
  development cycle with squash merge commit
  `322b6246d8aa349bb08c202484f34df6506416bf`.
- The follow-up preview workflow run `27876562110` published
  `2.4.0-preview.75` for all ten packages. Its first attempt hit the existing
  chaos integration test flake
  `Random_disconnects_under_load_lose_no_qos1_messages_with_a_persistent_session`;
  rerun attempt 2 passed and all ten preview packages indexed.
