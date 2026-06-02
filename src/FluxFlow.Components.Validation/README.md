# FluxFlow.Components.Validation

Reusable validation components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `json.schema-validator` | `Input` -> `Result`, `Valid`, `Invalid` | Validates a selected value with a JSON schema. |

The package does not know application payload types. Hosts register type aliases
and optional value selectors during registration.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterValidationComponents(options => options
        .RegisterType<AppMessage>("app.message")
        .UseValueSelector<AppMessage>("payload", (message, context) => message.PayloadText));
```

Basic configuration:

```json
{
  "type": "json.schema-validator",
  "inputType": "object",
  "schemaId": "orders",
  "schema": {
    "type": "object",
    "required": [ "id" ],
    "properties": {
      "id": { "type": "string" }
    }
  },
  "valueSelector": "input",
  "boundedCapacity": 128
}
```

Use `schemaPath` instead of `schema` when the host wants the package to read a
schema file. `payloadSelector` is accepted as an alias for `valueSelector`.

Invalid data emits a result and routes the original input to `Invalid`; it is
not reported as a processing error. Schema loading, value selection, value
conversion, and evaluation failures emit `FlowError` and the node continues
processing later messages where possible.

## Runtime Timing

Validation results use the package clock for `Timestamp`. Existing callers use
the default system clock. Hosts and tests can provide a deterministic clock
through registration:

```csharp
registry.RegisterValidationComponents(options => options
    .UseClock(validationClock));
```

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
