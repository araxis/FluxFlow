# Data Generator

Produces sample data from a JSONata expression and generator functions.
Configuration type: `dataGenerator`.

## Setup

Supply **Expression** and optionally **Locale** (default `en`). Open the shared
expression editor to preview sample input and message metadata with the generator's
functions and selected locale. Preview uses the same evaluator as the runner, with
a fresh generator for each evaluation. Random preview values need not match a later run.
Generator functions stay local to this component; ordinary Mapper previews are unchanged.

Inline and Base64 expressions can be previewed. File references remain exactly as
entered and are opened only by the runner. Syntax, locale and function-argument errors
appear in the shared result panel; correcting the expression allows the next preview
to run. Upstream error samples bypass generation, matching runtime behavior.
Save changes only the expression. Close discards the draft; sample input and metadata
are not stored in the workflow. A stale dialog cannot overwrite a changed selection or locale.

Locale uses the same Bogus resource lookup as the runner. Unknown names are flagged
without changing the saved value. Clear the field to restore the default `en`;
supported identifiers such as `sv` are retained exactly as entered.

## Ports

Connect a trigger to **Input** to request generated data. **Output** emits the result.
**Events** is the component event port; **Error** filters Output errors.
`OutputCapacity` defaults to 128.

Use Source for a fixed list of values, or Scheduler upstream for repeated generation.
