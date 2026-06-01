# Component Composition Docs

Date: 2026-06-01

Added `docs/12-component-composition.md` as the recommended composition guide
for reusable component packages.

The page documents:

- the preferred path from host-owned source/sink nodes to reusable packages
- what the host should own
- what packages should own
- common request/result, source, and observer shapes
- example graph compositions
- when to extract a new component package
- why component package additions should not force an engine release

Decision:

Keep this as one focused guide in the docs set. Package READMEs can stay short
and link back to the general composition guidance later if needed.
