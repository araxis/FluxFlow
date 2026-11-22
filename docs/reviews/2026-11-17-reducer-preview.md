# Reducer preview and validation

Reducer's Reduce and Equality properties now use the shared expression workspace and existing host evaluator. The Transformation plugin supplies portable sample definitions and preview providers. It shares expression compilation and variable binding with the runtime registration rather than implementing a second reducer.

## Behavior

- Reduce accepts an editable JSON sample with `state` and `input` and returns a candidate state. The existing `value` and `payload` aliases still refer to input.
- Equality accepts `previous` and `current` and returns a Boolean using the configured engine's existing type rules.
- Missing sample fields produce an error; explicit JSON null is supported. Compile, source and evaluation errors retain their stage and full exception details.
- Preview is a single independent evaluation. It does not advance running state, load Initial state files or update saved Initial state. The sample starts as an editable example and is never stored in workflow configuration.
- The common workspace omits message metadata and error-input controls for custom sample contexts. Its docking, formatting, error display, Save/Cancel and history behavior remain shared.
- Inline and Base64 sources can be previewed. File-backed sources remain deferred to the runner.
- The optional `PreviewSample` metadata defaults to null for older plugins. Existing Data Generator and Mapper previews retain their message workspace. The SDK is 1.0.0-alpha.8; Transformation is 1.0.0-alpha.2.

## Verification

All checks passed on .NET 8, 10 and 11:

- Full Studio suite: 6,114 tests across the three frameworks. Includes JSONata and C# reducer contexts, state and equality results, nulls, sample validation, expression errors, source guards, missing engines/providers, cancellation, metadata transport, stale editor protection, Save/Cancel, saved configuration and Undo/Redo.
- Reducer runtime suite: 15 tests across the three frameworks.
- Selected Reducer runner and execution log/trace suite: 57 tests across the three frameworks. Exercises unchanged-state suppression, reduction errors, equality errors, recovery with the last accepted state, Stop under an active scheduler, repeated Stop, retained observations and fresh Initial state on rerun.
- Packaged Transformation trial: installed-package loading, both preview providers, invalid samples, file guards and active Stop/rerun passed on .NET 8.0.34, 10.0.15 and 11.0.0. The consumer has no Transformation project reference and asserts a plugin load context.

Published .NET 11 Studio was checked in a separate browser project and plugin store. State 4 plus input 3 returned 7. Previous 0 compared to current 1 returned false. Malformed sample JSON reported an Input error and correction recovered. The sample-only layout showed no message panel. Save persisted the reduction expression; Cancel left Equality empty; Undo/Redo removed and restored the saved expression.

Existing user sessions and plugin stores were left untouched. All 63 protected Core/WFENG files match their baseline hashes. Existing Studio analyzer warnings remain; the packaged trial consumer built without warnings.

## Limits

Preview does not automatically feed a result into the next sample; users can copy the candidate into the sample state to try another step. As with the existing shared evaluator, synchronous expression execution cannot be interrupted mid-call. The editor ignores stale results and checks cancellation at the existing boundaries. Runtime state ownership, equality suppression and graceful Stop behavior are unchanged.

## Repeat packaged trial

Build `Code/eng/reducer-plugin-consumer/ReducerPluginConsumer.csproj` in Release and run each target executable with an isolated plugin store containing the packaged Transformation and Sources plugins. A successful run prints `REDUCER_PLUGIN_TRIAL_PASSED` with the runtime version.

## Follow-up review

No confirmed production defect or unnecessary abstraction was found. Preview and runtime share compilation, typed equality and variable bindings. The shared editor retains its existing stale-result, Save/Cancel and graph-history protection. No production changes were needed in this pass.

Added regression coverage for concurrent independent state samples, repeated preview without advancing state, array/object/null payload preservation, undefined reduction results, null equality results, and isolation of equality variables from reduction input and message metadata.

Verification passed on .NET 8/10/11:

- 3,756 selected Studio tests, including Reducer, Data Generator, expression evaluation and shared editor/hosting checks.
- 15 Reducer runtime tests.
- 57 Reducer runner and execution log/trace tests, covering error recovery, unchanged-state suppression, active Stop/rerun and retained observations.
- Installed-package preview and Stop/rerun trials passed again on all three runtimes.

This review changes only tests and this record. Configuration and Undo/Redo remain intact. All 63 protected Core/WFENG files still match their baseline hashes. Existing Studio analyzer warnings and the documented synchronous-evaluation cancellation limitation remain.
