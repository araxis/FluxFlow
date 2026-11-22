# JSONata Mapper

Transforms each incoming message value with an expression. Configuration type: `mapper`.
New nodes preset `Engine` to `jsonata`; imported configurations with another engine
retain their engine and show a general Mapper Inspector.

## Expression

The JSONata root is the incoming message value. With `{"Reading":21.5}`, both
`Reading` and `$.Reading` access the field. Field names retain their original casing.
Headers are available through `$headers`; message metadata has its runtime bindings.

```jsonata
{
  "Temperature": Reading,
  "Device": $headers.Device
}
```

Double-click the node or open **Expression** to use the full editor and evaluate sample
data. Evaluation uses a draft; Apply updates the component configuration with Undo/Redo.
File-backed expression sources are resolved by the runner.

## Ports

**Input** receives a message; **Output** carries its transformed value.
Enable **Handle errors** to transform error messages as well as successful messages.
**Error** is filtered Output; **Events** is the component event port.
`OutputCapacity` defaults to 128.
