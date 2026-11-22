# Data Generator preview and validation

Data Generator now uses the shared expression workspace with its existing runtime evaluator. Its portable field definition selects the Sources plugin preview provider and supplies only Locale. Ordinary Mapper previews retain their existing functions and evaluation path.

## Changes

- Added an optional, UI-independent plugin preview delegate. Existing field definitions and evaluation requests retain their previous defaults.
- Reused GeneratorExpression for generator functions, sample message metadata and locale handling. Each preview gets its own evaluator; random samples are not persisted.
- Consolidated Data Generator settings validation across the Property Grid, Advanced JSON and runner preflight. Unsupported locales fail before Run.
- Retained shared source parsing, error stages and complete exception details. Preview accepts inline and Base64 expressions; file references remain deferred to the runner.
- Kept Save/Cancel and graph history ownership in the shared editor. Changes to the selected component, graph revision or preview settings invalidate an outstanding editor save.
- Updated Sources to 1.0.0-alpha.2 and the portable plugin contract to 1.0.0-alpha.7.

## Verification

Passed on .NET 8, 10 and 11:

- Full Studio suite: 6,039 tests across the three targets, including preview transport, compile/evaluation errors, locale, missing providers, Save/Cancel, stale dialogs, configuration preservation and Undo/Redo.
- Data Generation runtime suite: 111 tests across the three targets.
- Selected runner, execution log and trace tests: 57 tests across the three targets. An invalid generated value is followed by a successful value, then Stop and rerun; retained observations and original configuration remain unchanged.
- Packaged Sources plugin consumer: loaded from an isolated installed-plugin store, not a Sources project reference. Preview, argument errors, file guards, recovery and two normal runner lifecycles passed on .NET 8.0.34, 10.0.15 and 11.0.0.

The .NET 11 Studio distribution published successfully. In an isolated browser project, locale `sv` produced a generated name and the expected integer; invalid function arguments appeared in the shared result panel. Cancel left the expression empty, Save applied it, Undo removed it and Redo restored it. Existing user tabs and the existing plugin store were left untouched.

The new packaged consumer built without warnings. The full Studio build still reports existing repository warnings. All 63 protected Core/WFENG files match the recorded baseline hashes.

## Boundaries

Preview is a sample evaluation, not a workflow execution. Random preview values may differ from later runs. File-backed expressions remain runner-only. The existing synchronous evaluator cannot be interrupted mid-expression; cancellation is checked before and after evaluation, and the shared editor ignores stale results.

## Repeat packaged verification

Build `Code/eng/data-generator-plugin-consumer/DataGeneratorPluginConsumer.csproj` in Release, then run each target's executable with an isolated plugin store containing the packaged Sources plugin and its normal dependencies. The consumer checks that Sources was loaded through the installed-plugin load context and prints `DATA_GENERATOR_PLUGIN_TRIAL_PASSED` only after both preview and runner checks succeed.

## Follow-up review

No confirmed production defect or unnecessary abstraction was found in the preview and validation path. The optional provider continues to use the existing evaluator and shared editor; no production changes were needed.

Added two focused regression scenarios:

- Concurrent previews using Swedish, English and omitted locale under English, Swedish and Persian host cultures. Generated weekday names must belong to the selected locale, and decimal output remains a JSON number. This detects locale leakage or silently ignoring the selected locale rather than merely checking for nonempty output.
- Stop while a scheduler continuously feeds Data Generator, followed by a fresh run. Both runs stop, repeated Stop preserves captured results, old results remain unchanged during the new run, input message IDs do not overlap and configuration is unchanged.

Review verification on .NET 8/10/11:

- 3,552 selected Studio tests passed, covering Data Generator preview and shared hosting/editor behavior, including Save/Cancel, stale edits and Undo/Redo. The build reports existing repository warnings.
- 111 Data Generation runtime tests passed.
- 60 selected generator runner, execution log and trace tests passed.
- Installed Sources package trials passed again on all three targets.
- All 63 protected Core/WFENG files still match the baseline hashes. This follow-up changes only tests and this review record; saved configuration and application runtime code are unchanged.
