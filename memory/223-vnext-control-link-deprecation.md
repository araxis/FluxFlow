# vNext Control Link Deprecation

Date: 2026-07-19

## Status

The twentieth bounded vNext milestone is implemented on local branch
`work/control-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone resolves Control by making canonical conditional links the only
new-definition filtering and branching primitive. It does not invent a
duplicate FlowValue control component.

## Canonical Boundary

- One conditioned output link replaces `flow.filter`; a nonmatching message is
  simply not sent to that target.
- Complementary conditioned output links replace `flow.when`; normal output
  fan-out evaluates each link independently.
- Composition compiles each distinct condition once per activation. Runtime
  condition failures reject only that link, report diagnostics, and do not stop
  sibling links or the host.
- Shared inputs and output fan-out remain canonical link/runtime behavior, so
  Control owns no distinct result-producing domain operation.

## Compatibility Surface

- `FilterNode<TInput>` and `WhenNode<TInput>` remain fully functional but are
  marked obsolete with canonical-link guidance.
- `RegisterFilter<TInput>()` and `RegisterWhen<TInput>()` remain available for
  legacy definitions and are marked obsolete with the same guidance.
- Both Designer metadata entries preserve their complete options, ports,
  aliases, and resource hints while adding `deprecated=true` and a migration
  reason. Hosts can hide them from new-node palettes without losing stored
  document rendering or validation.
- Existing expression-engine, typed context-factory, clock, queue, Events,
  Errors, and message-correlation behavior did not change.

## Compatibility And Versioning

- `FluxFlow.Components.Control` moves from `3.0.2` to `4.0.0` as the vNext
  compatibility line.
- `FluxFlow.Components.Control.Composition` moves from `1.4.0` to `2.0.0` as
  the canonical-definition compatibility line.
- No public declaration was added, removed, or signature-changed, so the
  source-declaration baseline remains unchanged.
- SDK package validation passes for Control `4.0.0` against published `3.0.2`
  and Control Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked repository state.

## Verification

- Control runtime tests: 30 passed, including both obsolete node attributes and
  all retained filter/branch behavior.
- Control Composition tests: 19 passed, including obsolete registration
  attributes, Designer deprecation metadata, hosted activation, resource
  resolution, aliases, and validation regressions.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,051 tests across 63 projects with no
  failures or warnings.
- Final controlled Debug and Release solution builds completed across 130
  projects with zero warnings and zero errors. The first Debug traversal
  reported one transient warning and the Release errors-only traversal exceeded
  its command bound without a compiler error; controlled incremental reruns
  completed cleanly after SDK build-server shutdown.
- A package-only net8 consumer restored Control `4.0.0` and Control Composition
  `2.0.0`, asserted runtime and Designer deprecation metadata, compiled the
  compatibility registrations under an explicit pragma, and printed
  `CONTROL_VNEXT_API_OK`.

## Deferred Boundaries

- No automatic definition rewrite, alternate condition engine, implicit
  mapper, result wrapper, universal error port, or runtime behavior change was
  introduced.
- Existing Control nodes remain available until a separately planned removal
  decision.
- Link-condition UI and persistence are part of the later Designer/hosting
  integration stage.

## Next Gate

Assess State as the next bounded component-family pass. Preserve its domain
state output separately from diagnostics while moving dynamic workflow values
to the canonical FlowValue/result conventions where appropriate.
