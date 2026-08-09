# Declarative Component Port Naming

Date: 2026-08-08

## Result

The typed component-binding DSL now uses `HasInput`, `HasSignalInput`,
`HasOutput`, and `HasEvents`. The short-lived public port-level `Add...`
methods were removed without obsolete aliases.

This is a terminology-only breaking refinement of the typed binding work in
memory 297. Runtime descriptors, activated bindings, event bridges, component
addresses, Designer metadata, registration identity, lifecycle ownership,
canonical JSON, and delivery behavior are unchanged.

## Decision

The selected `ITargetBlock`, `IFlowSignalTarget`, `ISourceBlock`, or event
source already exists on the activated node. A component author is not creating
that Dataflow port. The author is declaring that the component has an external
port with a chosen name and mapping it to the existing node member.

`Has...` therefore describes the authoring model more honestly than `Add...`:

```csharp
component
    .UseFactory(static _ => new UppercaseNode())
    .HasInput("Input", static node => node.Input)
    .HasOutput("Output", static node => node.Output)
    .HasEvents("Events", static node => node.Events);
```

`Bind...` was not selected because each call also creates immutable static
metadata before activation. `With...` was not selected because the operation
is a component-contract declaration rather than optional configuration.

## Scope And Boundaries

- Both `RuntimeComponentBindingBuilder<TNode>` and the advanced
  `RuntimeComponentInstanceBindingBuilder` expose only the four `Has...` names.
- Both designed binding builders mirror the same names and retain all display,
  grouping, ordering, summary, primary, cardinality, and attribute behavior.
- Internal `RuntimeComponentRegistrationBuilder` methods still use imperative
  `Add...` names because they actually add metadata and binding records to the
  registration snapshot.
- Engine's unrelated application-port runtime builder retains its own
  `AddSignalInput` operation.
- All 19 component composition families, 44 declarations, samples, and the
  package-only acceptance fixture use the declarative names.
- Options, resources, components, services, and standalone node APIs retain
  their existing `Add...` methods because those operations genuinely add an
  item or registration.
- No compatibility alias, reflection, scanning, dependency, package version,
  schema, or migration was introduced.

## Compatibility

This rename is intentionally source- and binary-breaking for the unpublished
typed builder surface. The public source-declaration baseline is updated through
the documented acceptance process. Published binary baselines stay unchanged,
so future package validation continues to require an appropriate major release
for the accumulated component-authoring break.

## Verification Evidence

- Focused Composition tests: 140/140 passed with zero warnings.
- Focused Designer tests: 121/121 passed with zero warnings.
- Focused release conventions, family matrix, metadata, and documentation
  tests: 59/59 passed with zero warnings.
- The accepted public API baseline records the intentional rename, and its two
  normal baseline tests pass without the acceptance environment variable.
- The CI-style Release build completes 134 projects with zero errors and zero
  warnings; the complete solution suite passes 2,563/2,563 tests across 66
  projects with zero warnings.
- The real package-only consumer packs, restores, verifies, builds, and runs
  all nine candidate packages in isolation. Engine, Fluent, SQL-file durable
  input/output, separate-process restart recovery, and receipt idempotency all
  pass.
- Full-solution formatting verification and the transitive vulnerability audit
  pass. Final whitespace, stale-name, and diff hygiene checks are clean.
