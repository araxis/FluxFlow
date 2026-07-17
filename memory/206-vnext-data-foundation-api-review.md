# vNext Data Foundation API Review

Date: 2026-07-17

## Decision

The first vNext foundation milestone is accepted for continued local
development. The public contracts are coherent with the agreed architecture,
the intended breaking Nodes changes are correctly versioned as a new major,
and the Data package remains transport-neutral and dependency-free.

This review closes the gate that prevented work on the canonical Composition
definition and address model. No Composition, Engine, Hosting, component, or
MQTT implementation was included in the foundation milestone.

## Requirement Review

| Area | Evidence | Decision |
|---|---|---|
| Package boundary | `FluxFlow.Data` targets net8/net10 with no project or package references; `DataFoundationBoundaryTests` guards the boundary. | Accepted |
| Value kinds | Tests cover null, Boolean, `BigInteger`, decimal, double, string, binary, all required temporal kinds, GUID, arrays, and objects. | Accepted |
| Deep immutability | Mutable byte, list, dictionary, and message-header inputs are copied; returned collections are immutable. | Accepted |
| Equality | Arrays are ordered; objects are ordinal and property-order independent; numeric kinds remain distinct; equal values have equal hashes. | Accepted |
| Canonical JSON | Literal snapshots prove sorted keys, kind preservation, invariant numeric text, signed-zero normalization, and culture independence. | Accepted |
| Content bytes | Ingress bytes are copied and retained with content type and encoding metadata; value-origin content explicitly has no byte representation. | Accepted |
| Lazy decode | Concurrent readers decode once; successful values and actual invalid-content failures are cached. | Accepted |
| Codec selection | Exact media type, structured suffix, media family, and binary fallback precedence are directly tested. | Accepted |
| Encoding | Quoted charsets, explicit encoding precedence, invalid encoding fallback, and UTF-8 behavior are tested. | Accepted |
| Message identity | Strong trace/message IDs, nullable causation, create defaults, `With(...)` propagation, new hop identity, timestamp, and JSON shape are tested. | Accepted |
| Results | `Kind`, computed `IsError`, optional workflow-safe `FlowError`, timestamp, details, and JSON shape are tested. | Accepted |
| Versioning | Data starts at `1.0.0`; Nodes moves from `1.2.1` to `2.0.0` for strong IDs and `FlowValue` headers. | Accepted |

## Review Fixes

- Added stable JSON contract snapshots for `FlowValue`, `FlowMessage<T>`, and
  error results.
- Added direct culture-independence and signed-zero canonicalization tests.
- Added explicit encoding precedence and actual invalid JSON failure-cache
  tests.
- Normalized out-of-range canonical decimal and natural JSON numeric failures
  to `JsonException` rather than leaking numeric implementation exceptions.
- Added a release-convention test that prevents `FluxFlow.Data` from acquiring
  Dataflow, Nodes, Composition, Engine, project, or package dependencies.
- Added direct proof that `FlowMessage.With(...)` advances the timestamp.

## Public Surface Review

The new Data API is additive because the package did not previously exist. Its
surface is intentionally small: immutable values, canonical serialization,
content codecs/catalog, and simple result/error contracts.

The Nodes change is intentionally breaking and therefore uses `2.0.0`:

- `MessageId` changes from `string` to a strong value type.
- `TraceId` and nullable `CausationId` are added.
- Headers change from `IReadOnlyDictionary<string, object?>` to
  `IReadOnlyDictionary<string, FlowValue>`.
- `FlowMessage.Create(...)` accepts an optional strong trace ID.

No existing component package was migrated, and no unchanged package version
was presented as compatible with the new Nodes major.

## Verification

- `FluxFlow.Data.Tests`: 32 passed.
- `FluxFlow.Nodes.Tests`: 41 passed.
- `FluxFlow.Release.Tests`: 93 passed.
- Complete Release solution test sweep: passed with no failures or skips.
- Controlled Debug solution build: 0 warnings, 0 errors.
- Controlled Release solution build: 0 warnings, 0 errors.
- Release preflight: passed for Data `1.0.0` and Nodes `2.0.0`.
- Isolated package dry-runs: archive inspection, feed-source verification, and
  temporary net8 consumer restore/build passed for both packages.
- Package artifacts remained in a temporary directory outside the repository.

## Next Milestone

Plan and implement the canonical Composition definition and one shared
ordinal, case-sensitive address model. Keep link parsing/conditions, stable
runtime ports, DI snapshots, revisions, components, and MQTT in later bounded
milestones unless the definition/address implementation requires a narrowly
defined supporting contract.
