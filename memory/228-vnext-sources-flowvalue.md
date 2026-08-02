# vNext FlowValue Sources

Date: 2026-07-19

## Status

The twenty-fifth bounded vNext milestone is implemented on local branch
`work/sources-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes generated and sequence sources canonical FlowValue
producers while preserving released typed standalone nodes and explicit typed
Composition registration paths.

## Canonical Runtime

- `FlowValueGeneratedSourceNode` emits configured immutable FlowValue items on
  one normal Output plus Events. It supports ordered lists, loops, item limits,
  initial/inter-item delays, bounded output, and fresh message identity.
- `FlowValueSequenceSourceNode` emits FlowValue objects containing `name`,
  `sequence`, `value`, `start`, `step`, and `timestamp` on the same canonical
  one-output shape.
- Both nodes retain natural zero-input source lifecycle semantics. A
  pre-canceled `StartAsync` does not consume their one-start state.
- Construction rejects invalid capacities, timing, loop limits, counts, and
  steps. Unexpected run-loop faults remain runtime/system faults. Neither
  canonical node exposes a universal Errors port.
- The internal FlowValue source pump owns bounded broadcast output, ordered
  lifecycle events, clean stop/completion, fault propagation, and idempotent
  disposal without introducing an Engine dependency.

## Composition And Designer

- Parameterless `RegisterGeneratedSource()` and `RegisterSequenceSource()` own
  the canonical fixed FlowValue output contracts.
- Generated `items` accepts one ordinary JSON value or an array and decodes each
  item once into immutable FlowValue data at activation.
- `RegisterGeneratedSource<TOutput>(nodeType)` retains the released generic
  generated source contract. `RegisterSequenceItemSource(nodeType)` retains the
  typed `SourceSequenceItem` contract from a separate compatibility extension
  class so it does not enter default Designer discovery.
- Canonical descriptors expose Events and no Errors port. The optional clock
  remains an exact host-owned keyed `TimeProvider` resource.
- Designer metadata reports fixed FlowValue outputs and explicitly omits the
  generic-only `outputType` diagnostic option.

## Compatibility And Versioning

- `FluxFlow.Components.Sources` moves from `3.1.2` to `4.0.0`.
- `FluxFlow.Components.Sources.Composition` moves from `1.5.0` to `2.0.0`
  because the default fixed output types and error surfaces change.
- `GeneratedSourceNode<TOutput>` and `SequenceSourceNode` retain their released
  options, typed Outputs, Errors ports, Events, lifecycle, and direct-use
  behavior.
- The source-declaration baseline records additive canonical runtime and
  compatibility registration declarations; no released declaration was
  removed or signature-changed.
- SDK package validation passes for Sources `4.0.0` against published `3.1.2`
  and Sources Composition `2.0.0` against published `1.5.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- Sources runtime tests: 37 passed.
- Sources Composition tests: 24 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,099 tests across 63 projects with no
  failures or warnings.
- Final controlled Debug and Release builds completed across 130 projects with
  zero warnings and zero errors. The cold Debug traversal reported one existing
  transient warning before the warm clean pass; the first cold Release command
  exceeded its command bound without an error, and the isolated rerun completed
  cleanly.
- A package-only net8 consumer restored Sources Composition `2.0.0`, asserted
  both canonical FlowValue registrations and typed compatibility registrations,
  ran `FlowValueGeneratedSourceNode`, verified message lineage, and printed
  `SOURCES_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- Source outputs remain live broadcast data, not durable replay storage.
- No fake input, implicit mapper, universal error port, polling/latest-value
  API, renderer, Engine dependency, or host-specific service framework was
  introduced.
- A single generated array item is represented by a nested array in the
  one-or-many `items` form; the outer array always represents the item list.
- Legacy Sources Composition `1.x` remains the stored-definition compatibility
  line.

## Next Gate

Assess Timers as the next bounded component-family pass. Preserve natural
zero-input timer sources and one-input temporal transforms while selecting
canonical FlowValue and normal-result contracts without exposing universal
Errors ports.
