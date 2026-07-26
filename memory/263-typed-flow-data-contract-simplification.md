# Typed Flow Data Contract Simplification

Date: 2026-07-26

## Scope

The repository-wide typed data-contract simplification is implemented on local
branch `work/simplify-flow-data-contracts`. It removes `FlowValue`,
`FlowResult<T>`, hidden `FlowContent` decoding, universal value contracts, and
universal error ports without changing the canonical application shape,
workflow addressing, Dataflow topology, transport ownership, or component
algorithms. No branch push, tag, package publication, pull request, or merge
was performed.

## Canonical Contracts

`FluxFlow.Nodes.FlowMessage<T>` is the one workflow envelope. It contains
exactly one active case: a value of `T` or a `FlowError`. Closed construction
prevents contradictory states, while `IsError`, `Value`, `Error`, `Match`,
`With`, and `WithError` provide the deliberately small public surface.
Successful nullable values remain distinguishable from errors through the
explicit case discriminator.

Derived messages preserve `TraceId`, optional `CorrelationId`, and immutable
ordinal string headers; receive a new `MessageId` and timestamp; and set
`CausationId` to the preceding message unless an explicit valid causation id is
provided. The strict JSON converter emits a stable flat envelope with
`isError`, `value`, and `error`, and rejects unknown, duplicate, missing, or
contradictory properties.

`FluxFlow.Data.FlowError` is a closed immutable transport-neutral record with
required normalized code, message, and category, explicit transient status,
and optional independently owned `JsonElement` details. Raw exceptions do not
cross workflow boundaries.

`FluxFlow.Data.FlowContent` now owns only exact `ImmutableArray<byte>` content
and normalized optional content type/encoding metadata. Mutable ingress buffers
are copied. The value-or-bytes dual state, original-representation flag, lazy
decode cache, codec catalog, codec interfaces, FlowValue codecs, and hidden
conversion failures were removed.

## Data And Error Semantics

- Normal components use typed command, event, result, snapshot, and content
  contracts rather than a universal recursive value.
- Expected operational failures travel through the normal output as
  `FlowMessage<T>` errors. Incoming errors are forwarded with preserved lineage
  unless an error-aware component intentionally handles them.
- There is no universal Error or Errors port. Existing diagnostic Events and
  runtime completion/fault channels retain their separate responsibilities.
- Known shapes use immutable CLR records. Schema-less JSON uses independently
  owned `JsonElement` only in JSON-oriented components.
- Dynamic CLR values remain explicit mapper or expression-engine results;
  `ExpandoObject`, `DynamicObject`, `JsonNode`, and `object` did not become new
  foundational contracts.
- Serialization and content conversion are visible workflow operations. Raw
  content can branch independently, while decoded content should be produced
  once before fan-out when several downstream nodes need the same view.
- Dataflow broadcasts share immutable envelopes and values. The runtime does
  not deep-clone arbitrary `T`; user-defined payloads follow the documented
  immutable-after-publication ownership rule.

## Repository Migration

Data, Nodes, Composition, Composition Hosting, Engine, Fluent, Fluent Hosting,
Coordination consumers, RequestReply, Retry, all maintained runtime component
families, all Composition adapters, HTTP hosting, MQTT core, both MQTT provider
adapters, samples, tests, Designer metadata, configuration binding, public API
documentation, and package documentation were migrated.

Representative replacements include typed Assertion, Mapper, JSON Validation,
State Reducer, Source, Timer, FileSystem, Routing, HTTP, Storage, Session,
Observability, Metrics, Projection, Expectation, Serialization, Payload,
Resilience, and MQTT contracts. Routing retains its mature Window,
Correlation, and Join algorithms behind typed public nodes. Retry now emits
`FlowMessage<RetrySignal>` directly rather than nesting `FlowResult`.

The production source no longer contains `FlowValue`, `FlowValueKind`,
`FlowValueCanonicalJson`, `FlowResult<T>`, `IFlowResult`, foundational content
codec infrastructure, removed lazy `FlowContent` members, universal Errors
ports, a Payload alias, or `FlowMessage<object>` as an untyped escape hatch.

## Dynamic-Data Benchmark

A temporary benchmark outside the repository compared the old universal tree,
owned `JsonElement`, typed CLR deserialization, `ExpandoObject`, a benchmark-only
read-only dynamic view, typed versus JSON predicates, and Dataflow four-way
fan-out for approximately 1 KB, 20 KB, and 1 MB nested payloads.

Construction/deserialization evidence:

| Payload | Old tree | JsonElement | Typed CLR | Expando | Dynamic view |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 KB / 3,000 iterations | 56.66 ms / 17.52 MB | 32.91 ms / 5.54 MB | 34.28 ms / 10.34 MB | 60.45 ms / 12.82 MB | 31.25 ms / 0.58 MB |
| 20 KB / 500 iterations | 97.51 ms / 38.49 MB | 68.46 ms / 13.11 MB | 75.73 ms / 25.18 MB | 109.65 ms / 30.52 MB | 57.57 ms / 0.10 MB |
| 1 MB / 12 iterations | 10.35 ms / 25.08 MB | 3.91 ms / 12.19 MB | 7.12 ms / 24.29 MB | 14.96 ms / 24.76 MB | 1.63 ms / 0.002 MB |

Typed C# predicates sustained roughly 23-49 million operations per second,
versus roughly 2.0-2.45 million for JSON property predicates. For material
payloads, conversion once before fan-out reduced time and allocation; the large
case allocated about 2.04 MB once versus 3.21 MB when converted in each branch.
Small setup noise did not justify an implicit conversion path.

The benchmark supports typed CLR values for normal work, explicit
`JsonElement` for schema-less JSON, no canonical Expando/custom dynamic value,
and explicit conversion before fan-out. The read-only dynamic view remains a
future expression-adapter optimization only if a real engine demonstrates the
need; no benchmark-only abstraction entered production.

## Package Impact

The manifest contains 62 packages. Dependency-closure analysis classified 56
as affected and moved each to the next major version. Six packages remain
unchanged because neither their public surface nor package dependency contract
changed: Resilience `1.0.0`, Mapping abstractions `1.0.3`, Expressions `2.1.3`,
Control `5.0.0`, Control Composition `3.0.0`, and Journal `2.3.6`.

The principal new versions are:

- Data `2.0.0`, Nodes `3.0.0`, Coordination `2.0.0`, Composition and Hosting
  `4.0.0`, Engine `4.0.0`, Fluent and Fluent Hosting `2.0.0`.
- RequestReply `2.0.0`; component Resilience and its Composition adapter
  `2.0.0`; HTTP AspNetCore `2.0.0`.
- MQTT `7.0.0`, MQTT Composition `4.0.0`, MqttNet `3.0.0`, and PulseMqtt
  `4.0.0`.
- Mapping, Assertions, Sources, Routing, Validation, FileSystem,
  Observability, Timers, HTTP, Serialization, Metrics, Projections,
  Expectations, Sessions, State, and Storage runtimes `6.0.0`.
- Their affected Composition adapters `4.0.0`, except Payloads,
  Serialization, Metrics, and Projections Composition at `3.0.0`.
- Payloads runtime `6.0.0`; Designer, Resources, Secrets, and Configuration
  `3.0.0`; Storage FileSystem and SqlFile adapters `4.0.0`.

Every affected project has updated release notes, README boundary guidance,
the matching changelog heading, and reviewed public API baseline coverage.
Intentional breaking declarations were not hidden with compatibility
suppressions or legacy shims.

## Verification

- Full Release solution test sweep: 1,702 passed in 65 projects, zero warnings.
- Release.Tests: 99 passed, zero warnings.
- Public API baseline tests: 2 passed.
- Controlled Debug and Release builds: 137 projects each, zero errors and zero
  warnings on final confirmation.
- Focused runtime/composition suites passed throughout the migration, including
  race-controlled Routing and Expectations completion coverage.
- Production and documentation scans found no unintended legacy contract or
  universal error-port references; the sole current-doc `FlowValue` mention
  explains that no replacement universal value exists.
- All 62 current packages packed from the controlled Release build into
  `%TEMP%\FluxFlowTypedContractsPackages263` outside the repository.
- All 56 affected packages passed release preflight and package dry-run against
  that complete local source plus NuGet.
- SDK package validation against every preceding published version produced 28
  clean binary-compatibility passes and 28 expected major-version API-break
  reports. No baseline restore, dependency resolution, packaging, or
  unexpected compatibility failure occurred.

Graph output is refreshed only as ignored local output. Final closeout includes
Release test confirmation after memory edits, `git diff --check`, status and
ignore inspection, neutral-name/text scanning, and a requirement-by-requirement
audit. The branch remains local and unpushed.

## Deferred Boundaries

This pass does not add a pull/latest-value API, supervision, Gate, RabbitMQ,
implicit link conversion, a custom dynamic object, a new Core package, native
preview unions, renderer behavior, transport redesign, or package publication.
Those remain separately planned decisions.
