# Change Filter

Emits a message only when its selected key changes. Configuration type: `changeFilter`.
Use it to suppress repeated consecutive readings or states.

## Setup

Set **Engine** to a registered expression engine, such as `jsonata`, and provide a
**Key selector** that returns a string. **Ignore case** controls string comparison
and defaults to false. This does not rename properties in the message.

## Ports

**Input** receives candidate messages; **Output** emits changes.
**Events** is the component event port; **Error** filters Output errors.
`OutputCapacity` defaults to 128.

For suppression across a retention window rather than just changes, use Deduplicate.
