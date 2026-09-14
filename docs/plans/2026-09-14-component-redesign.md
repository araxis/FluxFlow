# Component redesign from a framework-only baseline

The complete previous implementation is preserved on local `main` at `6a659218`.
This branch, `codex/core-only-component-redesign`, starts from that checkpoint.

## Boundary

All 44 `FluxFlow.Components.*` projects are removed, including their registration
extensions and transport/storage adapters. Their tests, workflow integration
suites, and the previous samples/templates are removed with them. The package
manifest now contains the 19 framework and infrastructure packages only.

The Core application/workflow/message/port APIs, base execution blocks,
Composition, JSON definitions, Engine, durability, hosting, expressions and
observability infrastructure are retained. Test-only nodes remain as fixtures
for framework behavior. This is removal of the old component implementation,
not a redesign of the framework or a change to its processing semantics.

Previous component documentation and review ledgers describe the `main`
checkpoint; they are historical references, not an inventory of this branch.
No old component package should be automatically restored to satisfy an example.

## Next work

1. Agree on the first small component and its exact input/output/error behavior.
2. Sketch the complete implementation and registration together, keeping local
   conversion inside the component and using the existing framework boundaries.
3. Implement only that component and meaningful behavior/registration tests.
4. Review ease of use and development before establishing a reusable pattern.
5. Add subsequent components one at a time; extract shared code only when actual
   repeated requirements justify it.

No replacement component, builder, catalog, or new abstraction is introduced by
this reset. Component selection and design are the next discussion.

## Verification of the starting point

- Restore and Release solution build passed with zero warnings/errors.
- All 3,070 remaining local regression tests passed in 31 reports, including
  the framework release/API checks. External/licensed database tests were excluded.
- All 19 remaining packages packed successfully; no component package remains
  in the manifest or output feed and no project reference points to a missing file.
- The 95 removed projects comprise 44 production component/extension projects
  and 51 test/sample projects. Component production source removal is 39,528 lines.
- No production file under Core, Definition, Engine or Adapters was modified.
- Local evidence is under `artifacts/core-only-20260914/` (ignored build output).

Both the full checkpoint and the redesign branch are local Git history. No remote
push or package publication was performed.
