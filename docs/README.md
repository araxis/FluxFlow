# FluxFlow.Engine Docs

The package README is the current public documentation for the standalone engine.

The previous detailed docs were moved to `memory/legacy-docs` because they still
describe source-app concerns and older public APIs. Keep them as reference while
rewriting the package docs around the current boundary.

## Rewrite Order

1. Getting started with neutral sample nodes.
2. Definitions and runtime model.
3. Node authoring and package extension model.
4. Scenario runner.
5. Generated API reference from the current source.

## Current Rules

- Use only protocol-neutral examples in public docs.
- Put planning notes and extraction history under `memory`.
- Keep component package ideas out of the base package docs until those packages exist.
- Regenerate API reference after every public API change.
