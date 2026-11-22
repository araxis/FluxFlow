# Reducer

Maintains JSON state using reduction and equality expressions. Configuration type: `reducer`.

## Setup

Provide **Engine**, **Initial state**, **Reduce expression** and **Equality expression**.
Initial state is JSON text or a runner-resolved file path.
The reduction expression uses `$state` and `$input`; equality compares `$previous`
and `$current`. These expressions use the reducer's runtime context, not the plain
Mapper sample context.

## Preview

Open **Reduce** or **Equality** in the shared expression editor. Reduce takes a sample
JSON object containing `state` and `input`; Equality takes `previous` and `current`.
Both values must be present; use JSON `null` when intended. Reduce returns the candidate
state and Equality returns a Boolean. They use the same compiler and variable bindings
as execution, including `$value` and `$payload` aliases for the reduction input.

Preview evaluates one step. It neither advances live state nor changes **Initial state**.
The initial sample is an editable example, not a copy of saved or running state. Copy a
result into the sample to try a subsequent step. Message metadata is not part of these
expression contexts. The configured engine determines expression syntax and type rules.

Compile and evaluation failures appear in the shared result panel. Save applies only the
expression; Close discards its edits. Samples are not saved into workflow configuration.
Inline and Base64 sources can be previewed; file references remain runner-only.

## Ports

**Input** supplies updates; **Output** carries changed state. A candidate considered
equal is neither stored nor emitted. Initial state is not emitted automatically.
**Events** is the component event port; **Error** filters Output errors.
`OutputCapacity` defaults to 128.

State belongs to the component execution. Do not treat it as persistent storage across restarts.
