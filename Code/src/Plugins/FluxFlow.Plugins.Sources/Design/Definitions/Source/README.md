# Source

Emits configured JSON values when triggered. Configuration type: `source`.

## Setup

**Values** contains JSON array text or a file path resolved by the runner. For example,
the field can contain `[1, 2, 3]`. File paths refer to the runner's environment, not the
browser. Studio preserves the configured source without opening runner files.

Relative paths use the runner application's base directory. Custom hosts can override
that directory through `AddSource(baseDirectory)`. They are not relative to the browser
or automatically relative to the saved project. Explicit `file:` and `inline:` prefixes
can disambiguate paths and JSON text.

The runner reads and validates file contents during Check and again when building a
new run. Missing files, malformed JSON and non-array contents fail before execution.
An active run uses a snapshot: editing or deleting the file does not change its values.
A new run reloads the file and starts from its first value.

The Values editor provides a visual list with Add, Edit and Remove. Choose each
value's JSON type explicitly: string, number, boolean, null, object or array.
Objects and nested arrays use JSON within their item editor; no payload schema
is inferred. Numbers are kept as JSON text without floating-point conversion.

Use the JSON tab to edit the complete array. Switching modes keeps the draft;
invalid JSON stays open for correction. Save applies the whole source as one
undoable edit, while Cancel discards all item edits. Unchanged sources retain
their original spelling and prefix. File sources remain runner-owned references.

## Ports

Connect a Scheduler or another trigger to **Input**. Each ordinary trigger emits the
next configured value on **Output**. The list does not wrap: after exhaustion there
is no ordinary output. Input errors pass through without consuming a value.
**Events** is the component event port; **Error** filters errors on Output.

Use Source for fixed sample data. Use Data Generator for generated values or Mapper
to transform incoming data. `OutputCapacity` defaults to 128.
