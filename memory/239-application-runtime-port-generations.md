# Application Runtime Port Generations

Date: 2026-07-20

## Status

Dynamic canonical application port generations are implemented locally on
branch `work/application-runtime-generations`. No push, tag, publication, pull
request, or merge was performed.

## Runtime Contract

- An exact address, direction, kind, and payload-type surface match reuses the
  current `ApplicationPortRuntime`. Direct handles remain stable while the
  revision replaces resources, components, attachments, links, and routing.
- A revision that adds, removes, or retypes a component port creates an
  isolated `ApplicationPortRuntime`, stages the complete revision there, and
  publishes that generation only after activation succeeds.
- `IApplicationRuntimeAccess.Ports` and `GetRequiredPorts()` expose the current
  generation. A caller holding an older runtime may finish in-flight work; the
  old runtime completes after its candidate drains and is disposed.
- Failed preparation or activation releases the unadopted generation and leaves
  the current generation available.
- Initial revision events remain bounded and are replayed before the first
  generation becomes visible. Later revision events cross the generation
  boundary according to activation order.

## Ownership

- Each candidate holds one reference to its port generation.
- The assembler holds one reference to the current generation.
- Publishing a replacement transfers the assembler reference to the new
  generation. The retiring generation is disposed only after the old candidate
  also releases it during drain/disposal.
- Assembler and candidate disposal remain independently ordered; neither can
  prematurely dispose a generation still owned by the other.

## Compatibility

- `FluxFlow.Engine` moved from `2.5.0` to additive `2.6.0`.
- No public declaration changed. The source-declaration baseline remains
  unchanged.
- The internal candidate constructor shape observed by the source scanner was
  retained, while the generation-aware construction path stays internal.
- Composition, Composition.Hosting, component packages, and their versions are
  unchanged.

## Verification

- Engine focused tests: 104 passed. Coverage includes same-surface identity,
  component add/remove, payload-type replacement, retired-generation
  completion, direct use of the new generation, resource cleanup, failed
  surface preparation preserving the active generation, and disposed-assembler
  adoption rejection.
- Composition.Hosting tests: 45 passed.
- Release tests: 94 passed after confirming the public source-declaration
  baseline remains unchanged.
- Controlled Debug and Release solution builds completed across 130 projects
  with zero warnings and errors.
- The first controlled build attempts encountered timeout-wrapper and file-lock
  interference from concurrent local builds. Only the identified FluxFlow
  build parent and .NET build servers were stopped; the unchanged controlled
  commands then passed.
- SDK package validation passed for Engine `2.6.0` against an exact local
  Engine `2.5.0` package built from commit `648db552` in a temporary detached
  worktree.
- Release preflight, package archive checks, package-only `net8.0` consumer
  restore/build, and local-feed verification passed against a fresh external
  source containing all 58 current packages.
- Temporary package and baseline outputs remained outside the repository. The
  detached worktree was removed after validation.
