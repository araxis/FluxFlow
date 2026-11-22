# Source readiness review

## Result

No production defect was found. Added .NET 11 to the Source runtime test project,
focused runner lifecycle tests and Source cases in the shared Advanced JSON regression
matrix. Runtime, configuration formats and plugin contracts are unchanged.

## Verified behavior

- Inline JSON preserves nulls, strings, booleans, objects, arrays and large integers.
- Explicit absolute/relative file references and implicit absolute paths work with
  extensionless filenames containing spaces and UTF-8 BOMs.
- Relative paths resolve from the runner application's base directory. Custom hosts
  can supply another base directory through the existing registration API.
- Check reads files, and a new run reads them again. Deleting a file after startup
  does not alter the active snapshot. Recreating it with different contents changes
  the next run without changing the saved reference.
- Empty and exhausted lists emit no additional values. Early Stop drains accepted
  triggers; reruns start from the first value, with distinct message identities and
  unchanged retained observations. Trace and causation fields remain linked to triggers.
- Missing files, malformed JSON, non-array files and deletion between Check and Run
  fail before execution. Repeated Stop cleans up failed startup; repairing the same
  file permits a subsequent run. Original failure diagnostics remain retained.
- Existing unit tests verify that error triggers pass through unchanged without
  consuming a value and that headers/correlation survive ordinary triggers.
- Shared editor tests cover typed values, Visual/JSON/file drafts, invalid arrays,
  mixed-case keys, unknown fields, exact unchanged strings, Save/Cancel, stale edits,
  component switching/disposal, saved wiring, project reload and Undo/Redo.

## Verification

| Suite | Frameworks | Passing tests |
| --- | --- | ---: |
| Source runtime | .NET 8/10/11 | 12 |
| Source runner plus log/trace boundary tests | .NET 8/10/11 | 78 |
| Shared component JSON, Source editors and plugin tests | .NET 8/10/11 | 372 |
| Existing loader and Source registration tests | .NET 8/10 | 60 |

The new Source case exposed a test-helper assumption: its invalid-type draft always
used `source`, which is valid when testing Source itself. The helper now selects a
different type and includes the rejected draft in assertion failures. No application
fix was needed. Existing Studio analyzer warnings remain.

All file trials used unique temporary directories and removed only their own files.
No user projects, tabs or plugin stores were modified. The editor checks used rendered
components; this pass did not include a fresh interactive browser trial. Core/WFENG
comparison covered 63 protected files with zero differences.
