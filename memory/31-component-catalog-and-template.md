# Component Catalog And Template

Date: 2026-05-31

## Goal

Plan the reusable component packages before scaffolding code.

This started as a planning-only step. The first package family has now been
implemented in `src/FluxFlow.Components.Mqtt`.

## General-Purpose Boundary

Design component packages from their category contracts, not from any one
consumer application. A consumer can prove whether the package is useful, but it
must not decide package-owned schemas, port names, request models, option
models, or node type names.

Reusable packages must not copy application workspace schemas, dashboards,
scenario definitions, storage models, or connection naming. Applications remain
free to project their own configuration shape into engine definitions.

## Package Shape

Use one class library per component category:

```text
src/
  FluxFlow.Components.<Category>/
tests/
  FluxFlow.Components.<Category>.Tests/
```

Each package may contain several nodes from the same category. For example,
`FluxFlow.Components.Mqtt` can contain an MQTT trigger/source, publisher/sink,
connection helpers, registration helpers, options, diagnostics, and tests.

## Project And Package Boundary

Each component family must be its own source project in the solution and its own
packable unit. A component package can include several related nodes from the
same category, but unrelated categories should not be bundled together.

Rules:

- one source project per component family
- one test project per component family
- one package artifact per source project
- each package owns its package identity, metadata, README, and release notes
- consumers reference only the component packages they need
- release automation can process several package projects in one run, but each
  project must remain a separate artifact

Create shared component helper projects only after at least two real component
families need the same helper contracts.

## Standard Package Template

Every component package should follow the same internal shape unless there is a
clear reason not to:

```text
FluxFlow.Components.<Category>/
  FluxFlow.Components.<Category>.csproj
  <Category>ComponentTypes.cs
  <Category>ComponentModule.cs
  <Category>ComponentRegistrationExtensions.cs
  Options/
    <Category>ComponentOptions.cs
  Contracts/
    request, result, and output records
    package-specific abstractions
  Nodes/
    component node classes
  Diagnostics/
    <Category>DiagnosticNames.cs
    <Category>EventNames.cs
  Validation/
    package-specific option validation
```

A buildable copyable version now exists at
`samples/FluxFlow.ComponentPackageTemplate`, with focused tests under
`tests/FluxFlow.ComponentPackageTemplate.Tests`.

Each test project should include:

```text
FluxFlow.Components.<Category>.Tests/
  <Category>ComponentModuleTests.cs
  <NodeName>Tests.cs
  <Category>OptionsTests.cs
  TestDoubles/
```

## Required Package Contracts

Each package should expose:

- stable node type names
- one `IFlowNodeModule` implementation
- one registry extension method
- option models and parsing helpers
- request, result, and output records for typed ports
- deterministic tests
- component diagnostics names
- component event names when workflow activity should be visible

Avoid:

- assembly scanning
- hidden static configuration
- app-specific workspace models
- dashboard/UI models
- live external services in normal unit tests

## Request, Options, And Result Pattern

Components should behave like small typed functions over engine ports:

- options records describe static node configuration
- request records describe each operation message
- output/result records describe emitted values or completed work
- diagnostics and events describe observation, not control flow

Default port conventions:

| Shape | Input ports | Output ports |
|-------|-------------|--------------|
| Source/trigger | none | `Output` with a package-owned output record |
| Transform | `Input` with a package-owned input record or primitive value | `Output` with a package-owned output record or primitive value |
| Sink/command | `Input` with an `<Action>Request` record | optional `Result` with an `<Action>Result` record |
| Utility | choose the smallest explicit shape | choose the smallest explicit shape |

Use `Input` as the default inbound port name. Put semantic meaning in the type,
not in custom port names. For example, an MQTT publish node should receive
`MqttPublishRequest` on `Input`, read static settings from
`MqttPublishOptions`, and optionally emit `MqttPublishResult` on `Result`.

Keep options and requests separate:

- options: static values parsed from node configuration during build/startup
- request: per-message values supplied by the workflow at runtime
- result/output: values emitted after processing or observation

Validation should happen at the right boundary. Options should fail early during
build/startup where possible. Requests should fail per message with clear
diagnostics and normal runtime failure behavior.

## Component Definition Template

Before implementing each component, write a small definition record in memory or
package docs:

```text
Component:
  Name:
  Package:
  Node type:
  Role: source | sink | transform | utility
  Inputs:
  Outputs:
  Options type:
  Request/input type:
  Result/output type:
  Configuration fields:
  Option validation:
  Request validation:
  Diagnostics:
  Events:
  Failure behavior:
  Test cases:
```

This keeps component design consistent and makes future migrations less guessy.

## Category Catalog

### MQTT

Package:

```text
FluxFlow.Components.Mqtt
```

Planned components:

| Component | Role | Node type | Contract shape | Notes |
|-----------|------|-----------|----------------|-------|
| MQTT Trigger | source | `mqtt.subscribe` | emits `MqttReceivedMessage` on `Output` | Subscribes to a topic/filter and emits messages. |
| MQTT Publisher | sink | `mqtt.publish` | receives `MqttPublishRequest` on `Input`; optional `MqttPublishResult` on `Result` | Publishes requests using static defaults from `MqttPublishOptions`. |
| MQTT Topic Filter | transform | `mqtt.topic-filter` | `Input` to `Output` with `MqttReceivedMessage` | Routes or filters messages by topic pattern without broker access. |
| MQTT Payload Decoder | transform | `mqtt.payload-decode` | `Input` to `Output` with package-owned payload records | Converts payload bytes/text into app-friendly values. |
| MQTT Payload Encoder | transform | `mqtt.payload-encode` | `Input` to `Output` with package-owned payload records | Converts app values into publishable payloads. |
| MQTT Connection Probe | source/utility | `mqtt.connection-probe` | emits connection status on `Output` and diagnostics | Emits connection status diagnostics on a schedule. |

Shared package pieces:

- connection profile options
- client adapter contract
- client factory contract
- request, result, and message DTOs
- topic matching helper
- reconnect policy options
- diagnostics and event names

Recommendation:

Use this as the first real package. The initial package now includes
adapter-backed publish and subscribe nodes; the first consumer migration should
validate whether the options and adapter surface need adjustment.

### Mapping

Package:

```text
FluxFlow.Components.Mapping
```

Implemented components:

| Component | Role | Node type | Contract shape | Notes |
|-----------|------|-----------|----------------|-------|
| Mapper | transform | `flow.mapper` | `Input` to `Output`, defaulting to `object` ports and supporting host-registered typed aliases | Maps each message with a host-provided expression engine and context factory. |

Shared package pieces:

- expression engine resolver registration
- type alias registration
- per-type mapping context factories
- mapper options reader
- diagnostics and error codes

Recommendation:

Keep this package focused on `flow.mapper` until a real consumer proves the
next primitive. Likely follow-ons are `flow.filter`, `flow.router`, and
`flow.assert`, but those should not be added until their contracts are clearer.

### Control

Package:

```text
FluxFlow.Components.Control
```

Implemented components:

| Component | Role | Node type | Contract shape | Notes |
|-----------|------|-----------|----------------|-------|
| Filter | transform | `flow.filter` | `Input` to `Output`, preserving input type | Emits only values whose expression evaluates to true. |
| When | router | `flow.when` | `Input` to `WhenTrue` and `WhenFalse`, preserving input type | Routes each value by expression result. |
| Assert | utility | `flow.assert` | `Input` to `Result`, `Passed`, and `Failed` | Emits `ControlAssertionResult` plus routed input values. |

Shared package pieces:

- expression engine resolver registration
- type alias registration
- per-type control context factories
- expression options reader
- diagnostics and error codes

Recommendation:

Keep scenario timing, journals, expected-event helpers, and isolated runtime
rules out of this package. Those remain host/product behavior. Use these nodes
as building blocks for cleaner host-side scenarios later.

### HTTP

Package:

```text
FluxFlow.Components.Http
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| HTTP Request | transform/sink | `http.request` | Sends an outbound request from input data and emits response data. |
| HTTP Poller | source | `http.poller` | Polls a URL on an interval and emits responses. |
| HTTP Response Filter | transform | `http.response-filter` | Routes by status code, header, or content metadata. |
| HTTP Header Mapper | transform | `http.header-map` | Builds request headers from input and configuration. |

Deferred:

- inbound web/server trigger nodes should stay out until the hosting boundary is
  clearer.

### FileSystem

Package:

```text
FluxFlow.Components.FileSystem
```

Implemented components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Directory Enumerator | source | `directory.enumerate` | Emits file and directory paths with metadata. |
| File Reader | utility | `file.read` | Reads file content as text or bytes and emits `FileReadResult`. |
| File Watcher | source | `file.watch` | Emits file-change events for a configured directory. |
| File Writer | sink | `file.write` | Writes request content or bytes to a file and emits `FileWriteResult`. |

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| File Copier | utility | `file.copy` | Copies files under the same path policy. |

Shared package pieces:

- path policy options
- overwrite/append behavior
- encoding options
- deterministic temp-directory tests

### Timers

Package:

```text
FluxFlow.Components.Timers
```

Implemented components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Interval Trigger | source | `timer.interval` | Emits `TimerTick` values at a fixed interval with optional initial delay, immediate first tick, and max tick count. |
| Schedule Trigger | source | `timer.schedule` | Emits `ScheduleTick` values from a five-field or six-field cron expression. |
| Delay | transform | `timer.delay` | Delays each typed input item and emits the same item unchanged. |

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Throttle | transform | `timer.throttle` | Limits output rate. |
| Debounce | transform | `timer.debounce` | Emits only after quiet periods. |

Recommendation:

This category is useful for workflow orchestration and can later add a
host-provided clock if interval tests need stronger determinism.

### Data

Package:

```text
FluxFlow.Components.Data
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| JSON Select | transform | `data.json-select` | Selects a value from JSON input. |
| JSON Map | transform | `data.json-map` | Maps JSON to JSON using configured rules. |
| Text Template | transform | `data.text-template` | Produces text from input variables. |
| Split | transform | `data.split` | Splits arrays, lines, or batches into items. |
| Batch | transform | `data.batch` | Groups items by size or time. |
| Filter | transform | `data.filter` | Applies a configured predicate. |

Boundary:

Keep domain-specific mapping rules in applications. This package should only
contain generic data-shaping components.

### Validation

Package:

```text
FluxFlow.Components.Validation
```

Implemented components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| JSON Schema Validator | transform/router | `json.schema-validator` | Validates a host-selected value, emits a validation result, and routes the original input to `Valid` or `Invalid`. |

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Required Field Check | transform | `validation.required-fields` | Checks configured fields. |
| Predicate Check | transform | `validation.predicate` | Runs a configured predicate and routes result. |
| Assertion Sink | sink | `validation.assert` | Useful for tests and local checks. |

Boundary:

Application-specific scenario runners stay outside this package until a second
real consumer needs them.

### Replay

Package:

```text
FluxFlow.Components.Replay
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Replay Source | source | `replay.source` | Emits recorded messages. |
| Recording Sink | sink | `replay.record` | Records incoming messages. |
| Replay Clock | utility | `replay.clock` | Controls playback speed and timing. |
| Replay Filter | transform | `replay.filter` | Selects messages from recorded streams. |

Boundary:

Keep storage format simple and package-owned. FluxMq scenario definitions stay
outside this package for now.

### Observability

Package:

```text
FluxFlow.Components.Observability
```

Implemented components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Logger | observer | `flow.logger` | Emits structured `FlowLogEntry` values from host-selected attributes. |
| Metrics | observer | `flow.metrics` | Emits `FlowMetricSnapshot` values with count, rate, timestamp, and optional size data. |
| Counter | observer | `flow.counter` | Emits `FlowCounterSnapshot` values with optional expression-backed filtering. |

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Diagnostic Sink | sink | `flow.diagnostics-sink` | Receives diagnostic-shaped messages and forwards them. |
| Event Sink | sink | `flow.event-sink` | Records workflow events for inspection. |
| Heartbeat | source | `flow.heartbeat` | Emits periodic status snapshots. |

Boundary:

Adapters for specific observability systems should be separate packages or
application-owned bridges. The base package should stay neutral and emit
package-owned records.

## Development Order Options

Option A: MQTT first.

- Best match for the first consumer migration.
- Proves real source/sink behavior.
- Needs adapter contracts before nodes.

Option B: Timers first.

- Fastest way to prove package template.
- No external service boundary.
- Less directly useful for the first consumer migration.

Option C: Files first.

- Useful in many projects.
- Good middle ground for source/sink behavior.
- Needs careful path safety decisions.

## Recommendation

Keep MQTT as the first real target, but scaffold the template so Timers or
Files could also use it without redesign.

For the next development step, do only the skeleton:

1. Add one component class library.
2. Add one test project.
3. Add module and registration extension shape.
4. Add one placeholder component definition document.
5. Add release workflow support for multiple independent package projects.
6. Do not implement live external behavior until the skeleton and tests are
   reviewed.

## Open Decisions Before Scaffolding

- First package to scaffold: MQTT, Timers, or Files.
- Whether package docs live in each package folder or under root docs.
- Whether package versions always match engine prereleases during alpha.
- Whether release workflow discovers packages by project metadata or explicit
  project list.
- Whether shared component test helpers deserve a separate test utility project.
