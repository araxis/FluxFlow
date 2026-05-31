# Documentation Consolidation

Date: 2026-05-31

## Decision

Move the existing detailed `docs` pages into `memory/legacy-docs` and keep only a
small clean docs entrypoint until the standalone package docs are rewritten.

The old docs contain useful explanations, but they also include source-app
examples, stale configuration names, and API entries that no longer exist in the
engine. Keeping them as public docs would mislead package consumers.

## Moved Pages

- `01-architecture.md`: keep the three-layer model and completion notes; rewrite examples with neutral resources.
- `02-getting-started.md`: keep the integer pipeline walkthrough; update package version and configuration section.
- `03-core-concepts.md`: keep node, error, and event concepts; remove old event constant references.
- `04-definitions.md`: keep JSON/link rules; remove component-specific examples.
- `05-runtime.md`: keep port, completion, and phase-ordering sections; rewrite resource examples.
- `06-mapping.md`: keep mapper contracts; replace component-specific payload examples.
- `07-scenarios.md`: keep event expectation flow; move external driver steps to future component packages.
- `08-hosting.md`: keep host lifecycle; update section names and hosting examples.
- `09-extending.md`: keep node factory patterns; move component package advice into its own page later.
- `10-api-reference.md`: regenerate from current source instead of editing by hand.
- `11-faq.md`: split into public package FAQ and internal roadmap notes.

## Public Docs To Rebuild

1. `docs/getting-started.md`
2. `docs/definitions.md`
3. `docs/runtime.md`
4. `docs/extending.md`
5. `docs/scenarios.md`
6. `docs/api-reference.md`

## Acceptance Rules

- Public docs must not mention removed source-app components as built-ins.
- Public docs must compile mentally against current source names and package version.
- README links must not point to stale moved pages.
- Memory may keep old context as long as it is clearly marked as legacy/reference.
