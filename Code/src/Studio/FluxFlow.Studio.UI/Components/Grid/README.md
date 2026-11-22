# Property Grid conventions

`StudioPropertyGrid` owns table layout, spacing, separators, compact actions and
keyboard focus styling. Feature editors supply rows; they do not recreate the grid.

## Rows and editors

- Put rows inside `tbody`. Use `property-section` with a spanning `th` for a group,
  and `property-row` with `th scope="row"` and `td` for a property.
- Use short, meaningful labels. Keep serialization names unchanged. Put units in
  the label or existing help rather than relying on unexplained numeric values.
- Use `OptionEditorField` for metadata-backed settings, `ConnectionSelect` with
  `PropertyCell=true` for typed references, and existing specialized editors for
  values that need them.
- Use `RelatedComponent` for a component-name reference restricted to a type in
  the current workflow. `ComponentSelect` lists compatible nodes, retains missing
  saved references as disabled choices and checks the target again before committing.
  It never replaces a missing reference automatically or inserts a default.
- Required plain-text fields use `ValidatedTextField`: blank drafts show the
  existing compact diagnostic without removing the last committed value or
  changing history. Preserve valid text exactly; validation must not trim it.
  Optional text, explicit-empty contracts and typed connection selectors retain
  their own behavior. Do not turn host-owned resource keys into project connection
  selectors unless the existing APIs provide compatible project resources.
- Use `OptionEditorKind.Integer` for integral counts, capacities, ports and
  integer-valued timeouts that must not pass through floating-point conversion.
  It reuses the flat validated text field,
  retains invalid drafts, validates whole numbers and metadata bounds, and writes
  exact JSON integers. Equivalent numeric edits preserve saved spelling and type;
  rendering does not insert defaults. Clearing an optional explicit value removes
  it; required integers retain their last committed value and show a diagnostic.
  The Int64 range is implicit; supply narrower bounds matching the actual contract
  (for example Int32 capacity, UInt16 prefetch or UInt32 session expiry). Default
  hints appear only for omitted settings. Fractional numeric contracts may use
  `Number`; integer properties must not use it merely to obtain a numeric input.
  When clearing an optional integer restores a default, validate that default
  against the current bounds before removing the explicit value. Features can
  supply dependent bounds through `IComponentDescription.GetInspector`; keep
  the relationship in the feature's metadata and verify it against its native
  configuration check. Do not silently clamp either saved value.
- Use `OptionEditorKind.Duration` for TimeSpan settings. The shared duration field
  keeps invalid text as a local draft, links its diagnostic to Validation and
  preserves valid source spelling. Required durations reject empty drafts.
  Supply `MinDuration` and `MaxDuration` in feature metadata when the native
  contract has bounds. Opt into `AllowInfiniteDuration` only when the contract
  supports `Timeout.InfiniteTimeSpan`; it is not a general negative-duration
  exception. These constraints prevent invalid edits without repairing saved
  values automatically. Unconstrained duration fields retain their existing
  behavior, and native component validation remains authoritative at execution.
- Use `OptionEditorKind.DateTimeOffset` for scheduled instants. It shares draft
  handling and Validation links with Duration through `ValidatedTextField`.
  Preserve the user's valid date/time text and offset rather than normalizing it
  to the browser's local time. Keep runner-specific constraints in existing
  component validation.
- Use `OptionEditorKind.Base64` for byte sequences stored as Base64 strings.
  The shared field supports optional named presets through `Choices` and decoded
  byte bounds through `Min`/`Max`. Custom values reuse `ValidatedTextField` and
  its native form validation. Choosing Custom is display-only; equivalent bytes
  preserve the original spelling rather than normalizing the saved string.
  Presets and limits belong to feature metadata, not shared rendering. Use a
  normal selector when the contract supports only a fixed set of text values.
- Grid input editors use `Variant.Text`, `Underline=false` and dense spacing.
  Do not add filled backgrounds, individual field borders or floating labels.
  Preserve visible keyboard focus, accessible names and disabled states.
- Row and group icon actions use the grid's shared spacing and icon size. Group
  headings with actions use `property-section-with-actions` and
  `property-section-heading`.
- Ordinary dialog forms use outlined inputs. Toolbar dropdowns use the shared
  borderless toolbar control. These are separate contexts, not competing grid styles.

## Editing behavior

- Every component Inspector exposes one whole-component Advanced JSON action.
  `ComponentJsonEditor` supplies the shared fallback; configurators with an existing
  complete editor declare `ProvidesComponentJson` to avoid duplicate actions.
  Field-only JSON actions use specific labels such as Topics JSON or Routes JSON.
  The fallback keeps Type fixed, validates through the existing authoring registrar
  and preserves unsupported/unknown fields. It does not activate runtime factories
  or claim resource, expression or hardware validation. Drafts are isolated until
  Save; no-op, Cancel, stale selection and disposal do not create history entries.
- Opening or rendering an editor must not write defaults into configuration.
  Hidden conditional fields retain their stored values.
- Structured Visual/JSON dialogs can receive `IsVisible` against the current
  draft. Reuse the feature's Inspector predicate rather than duplicating its
  conditions. Field edits refresh visibility without rebuilding unrelated fields;
  JSON-to-Visual switching applies the same rule to the newly parsed draft.
  Removed fields reject late callbacks and retain their saved values.
- Structured dialogs validate the live visual form before Save or switching to
  JSON, so an invalid local field draft is not silently replaced by its last valid
  configuration value. Cancel remains available. Conditional fields unregister
  from the form when removed; their retained configuration still passes through
  the feature's existing save validator. `ValidatedTextField` participates in the
  native form validation while keeping its compact diagnostic instead of adding
  a second error paragraph below the input. Diagnostic icons expose the error
  explanation in their tooltip and accessible description.
- Connection groups can supply `IsVisible` against their current draft. Keep the
  rule in the feature description, not in shared rendering. Retain saved conflicting
  fields for repair when the runtime rejects settings from a different mode; unknown
  modes must not make their configuration inaccessible.
- Use a group's optional `RowsComponent` when visual fields project an existing
  connection value, such as a BLE endpoint's address and address type. It receives
  the group's `Configuration` draft and `OnChanged`; the shared grid retains commit,
  history and stale-group checks. Keep conversion in the feature slice and do not
  introduce presentation-only properties into saved configuration.
- A property diagnostic with a component address links to Validation. Without a
  component address it shows a focusable error icon with an accessible explanation
  and native tooltip, without creating a misleading component validation record.
- For relationships between fields, keep validation in the owning feature and
  reuse its native configuration validator. `OptionPropertyRows.ValidationMessages`
  renders field messages through the shared diagnostic; `DiagnosticAddress` links
  Inspector errors to Validation. A structured dialog can use the same rule through
  `FieldValidation`, showing draft-only icons and rejecting Save with its readable
  message. Invalid combinations stay repairable without automatic value changes.
  A changed draft clears the preceding Save error; native validation runs again on
  the next Save. Do not embed feature-specific rules in shared grid rendering.
- Opt into `AllowEmptyText` only when an empty string is a meaningful supported
  value. Such fields distinguish Empty from Not set, expose Set empty when absent,
  and Reset to remove the property. Clearing text stores an empty string; unchanged
  blank input does not insert a value. Other text fields retain their removal behavior.
- Preserve property spelling, unknown fields, explicit nulls, credentials and
  original binary bytes. Display formatting is not a configuration migration.
- Keep connection choices filtered by supported type. Preserve unavailable saved
  references rather than replacing them with the first valid choice.
- Collection groups expose Add/Edit/Remove. Stage dialog edits in a draft, and
  apply one coherent change on Save. Cancel must not modify the original value.
- Use `StringCollectionPropertyGrid` for metadata-backed string lists, supplying
  the feature's existing validation. Unsupported saved shapes retain JSON editing.
- Use `StringMapPropertyGrid` for named string values. Supply the feature's name
  comparer and native validation; preserve key spelling and exact values. Sensitive
  maps show names only in the grid and mask values in the entry dialog. The shared
  dialog uses form validation so successive invalid drafts remain visible.
  A feature with an encoded string contract may supply paired `ReadEntries` and
  `WriteEntries` callbacks. Keep parsing and encoding in that feature's slice;
  the shared editor continues to own dialogs, no-op detection and stale-result
  guards. Return null for saved representations that cannot be edited safely.
  Preserve unchanged entries' source spelling and retain the native value shape.
  Advanced JSON always edits that native representation, not a projected map.
- Release the unfinished-edit guard before invoking a parent connection commit.
  Connection commits use the graph's proposal API, which requires an idle editor.
- Use existing graph edit and history APIs. Check the target and graph revision
  after asynchronous dialogs; reject results for replaced or disposed editors.
- Expression dialogs also retain the selected node/link and evaluation context.
  A changed engine or condition mode invalidates a pending save. Repeated open
  actions must not create competing dialogs for the same field.
- Expression metadata may disable live preview with `PreviewExpression=false`
  when the shared evaluator cannot reproduce the component's context. This keeps
  source editing, formatting and the Inspector preview in the same shared editor
  without presenting mapper results as generator or reducer behavior. Static
  expression syntax may be supplied by `Syntax`; it does not write an Engine key.
- Use `ExpressionResult` metadata when the native expression returns String or
  Boolean rather than a mapper value. Typed component expressions still read
  native source formats; they are not link conditions. The shared preview checks
  the native return contract, labels it, and rejects stale result-kind changes.
  In message previews, the Input selector distinguishes a value from an error
  envelope. Both editable samples belong to the dialog; saving applies only the
  expression. Reducer state and retry scheduling are not simulated.
- No-op saves, including JSON formatting changes, must not invoke change callbacks
  or discard redo history. Read-only snapshots stay read-only in every editor mode.
- Advanced JSON is optional. Visual edits preserve fields the visual editor does
  not understand. Invalid JSON remains a draft and must not replace saved settings.
  Component Inspectors use `ComponentJsonAction` for the flat, consistently named
  action row. Specialized editors retain their existing dialog and commit handlers;
  other components use `ComponentJsonEditor`. Mark a configurator's
  `ProvidesComponentJson` capability only when it edits the complete component.
  Field-only JSON actions should name their field instead.
  Connection descriptions and field editors supply semantic validation through
  the shared text dialog's `ValidateJson` callback. It receives the native JSON
  value; Undefined represents explicit clearing. Failed validation keeps the draft open for
  repair or Cancel; final commits still check the target, revision and contract.
  Validators must not include credentials or supplied secret values in errors.
  String lists and maps apply their entry rules to JSON edits as well as visual
  edits. Encoded collections supply their native validator independently of the
  visual reader, so valid advanced values such as repeated headers remain editable.
- Feature-owned plain-text constraints may use `OptionEditorModel.ValidateText`.
  The shared field combines them with required-value validation and retains invalid
  drafts without changing saved configuration. Optional empty values still reset
  the property; do not use a required field just to reject whitespace-only text.
  Dependent metadata changes revalidate the current draft without committing it or
  replacing it with the saved value. A changed saved value still refreshes the draft.
  Direct `ValidatedTextField` consumers can set `PreserveWhitespace` when whitespace
  is meaningful data, such as an AMQP routing key. Disabled fields reject callbacks.
- Property help remains outside the scrolling settings grid, but is height-bounded
  and independently scrollable. Keep the help region keyboard-focusable; long help
  must not squeeze the editable grid out of a narrow or short Inspector.
- Select editors retain an unlisted saved value as a disabled choice. Keep its
  original text and representation until the user explicitly selects another
  choice; different enum casing or numeric representation is not automatically
  invalid. Native configuration validation remains responsible for validity.
- Use `OptionEditorKind.JsonSource` for values that follow the existing JSON-source
  contract, rather than a generic text popup. It distinguishes inline JSON from a
  runner file reference without opening files. `JsonArrayOnly` constrains Source
  values while allowing arbitrary Reducer initial-state JSON. Keep both drafts
  when switching modes and preserve exact unchanged source text. Payload source
  text is data: formatting edits are explicit edits, unlike formatting a component
  configuration object in Advanced JSON.
  Array sources use the shared visual value list with explicit JSON types, while
  retaining a whole-array JSON tab and the file-source mode. Item dialogs edit the
  parent draft only; the source dialog owns the single configuration commit.
  Keep numbers as JSON tokens, not doubles, and preserve duplicate object members.
  Do not infer a form schema for arbitrary nested objects or arrays.
- Use `property-value-actions` to align a read-only value preview and compact row
  actions. Long previews may be abbreviated for display only; the editor retains
  the complete value. Keep row actions visible at narrow widths.

## Verification

The browser app retains framework resource strings with `UseSystemResourceKeys=false`
so native authoring-validation exceptions display readable messages. Do not translate
resource keys in individual editors or alter captured engine log messages.

Review the rendered Inspector at narrow widths and with keyboard navigation.
Verify conditional fields, typed references, cancellation, stale targets,
Undo/Redo and save/reopen for the affected feature. Regression tests should cover
configuration and editing contracts, not merely repeat the component markup.

The configuration editor inventory is in
[`../../docs/configuration-editor-inventory.md`](../../docs/configuration-editor-inventory.md).
