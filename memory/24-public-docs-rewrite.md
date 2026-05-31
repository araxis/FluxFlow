# Public Docs Rewrite

Date: 2026-05-31

## Summary

Replaced the placeholder docs entrypoint with a focused public docs set for the
standalone engine package.

Added docs:

- `docs/01-getting-started.md`
- `docs/02-definitions-and-links.md`
- `docs/03-node-authoring.md`
- `docs/04-package-authoring.md`
- `docs/05-hosting-and-observability.md`
- `docs/06-workspace-projection.md`
- `docs/07-validation-and-errors.md`

## Decisions

- Use the neutral sample app as the main runnable thread.
- Keep public docs protocol-neutral.
- Keep app workspace metadata outside `ApplicationDefinition`.
- Document package module registration as the current component-package pattern.
- Leave generated API reference for a later pass.

## Next Step

Add deeper reference docs only where users need exact behavior:

- runtime states
- JSON conversion details
- expression mapping details
- package versioning guidance
