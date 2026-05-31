# Component Catalog And Template

Date: 2026-05-31

## Goal

Plan the reusable component packages before scaffolding code.

This step is planning-only. No class libraries or component implementations are
created in this step.

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
    package-specific abstractions
  Nodes/
    component node classes
  Diagnostics/
    <Category>DiagnosticNames.cs
    <Category>EventNames.cs
  Validation/
    package-specific option validation
```

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
- deterministic tests
- component diagnostics names
- component event names when workflow activity should be visible

Avoid:

- assembly scanning
- hidden static configuration
- app-specific workspace models
- dashboard/UI models
- live external services in normal unit tests

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
  Configuration:
  Diagnostics:
  Events:
  Failure behavior:
  Test cases:
```

This keeps component design consistent and makes future migration from FluxMq
less guessy.

## Category Catalog

### MQTT

Package:

```text
FluxFlow.Components.Mqtt
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| MQTT Trigger | source | `mqtt.trigger` | Subscribes to a topic/filter and emits messages. |
| MQTT Publisher | sink | `mqtt.publisher` | Publishes incoming messages to a configured topic. |
| MQTT Topic Filter | transform | `mqtt.topic-filter` | Routes or filters messages by topic pattern without broker access. |
| MQTT Payload Decoder | transform | `mqtt.payload-decode` | Converts payload bytes/text into app-friendly values. |
| MQTT Payload Encoder | transform | `mqtt.payload-encode` | Converts app values into publishable payloads. |
| MQTT Connection Probe | source/utility | `mqtt.connection-probe` | Emits connection status diagnostics on a schedule. |

Shared package pieces:

- connection profile options
- client adapter contract
- client factory contract
- message DTO
- topic matching helper
- reconnect policy options
- diagnostics and event names

Recommendation:

Use this as the first real package once FluxMq-side feature work settles,
because it validates real source/sink behavior and adapter boundaries.

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

### Files

Package:

```text
FluxFlow.Components.Files
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| File Reader | source | `files.read` | Reads one file or a configured file set. |
| File Writer | sink | `files.write` | Writes payloads to files. |
| File Appender | sink | `files.append` | Appends records to an existing file. |
| File Watcher | source | `files.watch` | Emits file-change events. |
| Directory Enumerator | source | `files.enumerate` | Emits file paths and metadata. |

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

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Interval Trigger | source | `timers.interval` | Emits ticks at a fixed interval. |
| Schedule Trigger | source | `timers.schedule` | Emits ticks based on a schedule expression. |
| Delay | transform | `timers.delay` | Delays each input item. |
| Throttle | transform | `timers.throttle` | Limits output rate. |
| Debounce | transform | `timers.debounce` | Emits only after quiet periods. |

Recommendation:

This is the easiest category for deterministic package-template tests because
it can use a fake clock.

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

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| JSON Schema Validator | transform | `validation.json-schema` | Validates JSON input and emits valid/invalid results. |
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

### Diagnostics

Package:

```text
FluxFlow.Components.Diagnostics
```

Planned components:

| Component | Role | Node type | Notes |
|-----------|------|-----------|-------|
| Diagnostic Sink | sink | `diagnostics.sink` | Receives diagnostic-shaped messages and forwards them. |
| Event Sink | sink | `diagnostics.event-sink` | Records workflow events for inspection. |
| Counter | transform/sink | `diagnostics.counter` | Counts items and emits metrics diagnostics. |
| Heartbeat | source | `diagnostics.heartbeat` | Emits periodic status diagnostics. |

Boundary:

Adapters for specific observability systems should be separate or deferred
until the base diagnostics package proves useful.

## Development Order Options

Option A: MQTT first.

- Best match for FluxMq migration.
- Proves real source/sink behavior.
- Needs adapter contracts before nodes.

Option B: Timers first.

- Fastest way to prove package template.
- No external service boundary.
- Less directly useful for FluxMq migration.

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
5. Add release workflow support for multiple package projects.
6. Do not implement live external behavior until the skeleton and tests are
   reviewed.

## Open Decisions Before Scaffolding

- First package to scaffold: MQTT, Timers, or Files.
- Whether package docs live in each package folder or under root docs.
- Whether package versions always match engine prereleases during alpha.
- Whether release workflow discovers packages by project metadata or explicit
  project list.
- Whether shared component test helpers deserve a separate test utility project.
