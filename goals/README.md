# Executable Goals

Every implementation goal accepted for execution is recorded here before
production work begins. New goals use a dated, descriptive directory containing
a proper `README.md`, for example:

```text
goals/2026-08-01-example-goal/README.md
```

The goal README is the authoritative scope for its round: it records required
behavior, exclusions, documentation, tests, and completion gates so later
implementation cannot silently expand or drop requirements. Older standalone
Markdown goal files remain valid historical records and are not moved merely to
match the newer convention.

Goal files are historical records. Completion evidence belongs in the matching
`memory/` entry and repository documentation; the original goal remains an
unchanged statement of intent.
