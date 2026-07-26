# Typed Data-contract Migration

This guide covers the breaking release that removes the universal workflow
value/result model and adopts typed `FlowMessage<T>` value-or-error contracts.
It follows the earlier canonical application migration; the flat `Resources`
and `Workflows` document shape is unchanged.

## What Changed

Removed from the maintained public model:

- the universal recursive workflow value and its kind/canonical JSON helpers;
- the universal result interface and result wrapper;
- lazy raw-content codecs, catalogs, registrations, and decoded-value caches;
- universal component Errors ports;
- component APIs whose only purpose was converting typed values into the
  removed universal model.

The replacement is not another universal data object. Normal components use
their actual CLR contracts. `FlowMessage<T>` itself carries either T or
`FlowError`. JSON and byte representations are explicit boundaries.

## Message Migration

Before, an operation commonly produced a message containing another result
wrapper and callers inspected `Kind`, `IsError`, `Error`, and `Value` on that
wrapper. Now the message is the discriminator:

```csharp
FlowMessage<Order> input = FlowMessage.Create(order);

FlowMessage<Invoice> output = input.With(invoice);

FlowMessage<Invoice> failure = input.WithError<Invoice>(
    new FlowError(
        "invoice.invalid",
        "Invoice validation failed.",
        "validation"));

if (failure.IsError)
    Console.WriteLine(failure.Error!.Code);
```

Replace payload aliases with `Value`. Use `Match` when both cases must be
handled. Derived messages preserve trace/correlation/headers, create a new
message ID, set causation to the input message ID, and assign a new timestamp.

Headers are now immutable ordinal strings. Move nested documents to typed
payloads and keep trace, message, causation, correlation, and timestamp in their
first-class envelope properties.

## Error Routing

Remove links to universal Errors ports. Route the normal output by message or
typed-result properties:

```json
{
  "Workflows": {
    "Orders": {
      "Send": {
        "Type": "http.client",
        "Output": [
          { "Port": "HandleResponse.Input", "Condition": "isError == false" },
          { "Port": "RecordFailure.Input", "Condition": "isError == true" }
        ]
      }
    }
  }
}
```

Expected business/protocol variants remain fields or discriminated records in
the declared result. Processing failures use `FlowError.Code`, `Category`,
`IsTransient`, and optional JSON details.

## Typed Component Migration

| Former boundary | Maintained boundary |
|-----------------|---------------------|
| Universal mapper node | `FlowMapperNode<TInput,TOutput>` or `JsonMapperNode` |
| Universal assertion node | `AssertionNode<T>` or `JsonAssertionNode` |
| Universal schema validator | `JsonSchemaValidatorNode` over `JsonElement` |
| Universal routing nodes | generic Window/Correlation/Join nodes or explicit JSON specializations |
| Universal state reducer | `StateReducerNode<T>` or `JsonStateReducerNode` |
| Universal generated/timer values | typed generated values, `SequenceItem`, and typed timer ticks |
| Directory/watch documents | `DirectoryEntry` and `FileChange` |
| Nested operation result wrapper | direct typed output or `FlowError` in the message |

Known CLR values no longer require boundary conversion. Select a JSON
specialization only for schema-less JSON workflows.

## FlowContent Migration

`FlowContent` now contains owned immutable bytes, optional content type, and
optional encoding. Remove codec registration and calls that request a cached
decoded value.

```csharp
var content = FlowContent.FromBytes(bytes, "application/json", "utf-8");
```

Add explicit Serialization nodes instead:

```json
{
  "Workflows": {
    "Inbound": {
      "Parse": {
        "Type": "json.parse",
        "Input": "Receive.Output",
        "Output": ["Validate.Input", "AuditJson.Input"]
      }
    }
  }
}
```

The parse occurs once before fan-out. To keep raw bytes, fan out the receive
output to a raw branch and the parser. `FlowContent.FromBytes` copies incoming
memory, and detached `JsonElement` values own their lifetime.

## Mapping and Expressions

Typed expressions now receive typed values directly:

```csharp
var mapper = new FlowMapperNode<Order, Invoice>(
    new MapperOptions
    {
        Expression = "new Invoice(input.OrderId, input.Total)"
    },
    engine);
```

The configuration registration `data.map` is explicitly JSON-oriented and uses
`JsonMapperNode`. A mapper may deliberately return a CLR record, dictionary, or
`ExpandoObject`; dynamic values are not created implicitly. Expression engines
own any language-specific projection or internal read-only dynamic view.

## Composition Metadata

Update custom component registrations so metadata and runtime ports declare the
same T. Remove metadata for universal Errors, Failed, Passed, Valid, or Invalid
ports that previously duplicated conditions over one operation result. Retain
Events metadata and host-owned resource picker hints.

Configuration remains flat:

```json
{
  "Resources": {
    "Expressions": {
      "Default": { "Type": "expression.engine" }
    }
  },
  "Workflows": {
    "Orders": {
      "Map": {
        "Type": "data.map",
        "Expression": "...",
        "Engine": "Resources.Expressions.Default",
        "Input": "Receive.Output",
        "Output": "Validate.Input"
      }
    }
  }
}
```

Do not add `Composition`, `Nodes`, or root `Links` wrappers. Do not rely on
automatic type conversion between linked ports.

## Package Versions

Directly changed packages and their dependency closure use new major versions.
The central transitions are Data 2.x, Nodes 3.x, Composition/Hosting/Engine
4.x, Fluent 2.x, typed component runtimes 6.x, and their current composition
adapter majors. MQTT core moves to 7.x. See `CHANGELOG.md` and
`eng/packages.json` for every package-specific version.

The standalone Resilience and Mapping abstractions, Expressions, both Control
packages, and Journal remain on their existing versions because neither their
source nor packed dependency closure changed.

## Migration Checklist

1. Replace universal values with owned CLR records or explicit `JsonElement`.
2. Replace nested result messages with direct `FlowMessage<T>` outputs.
3. Route errors using `FlowMessage.IsError` and `FlowError` fields.
4. Replace payload aliases with `Value` and update lineage-aware derivation.
5. Convert headers to strings and move structured data into the payload.
6. Replace lazy content decoding with explicit serialization nodes.
7. Decode before fan-out and branch before decoding when raw bytes are needed.
8. Update node and Composition port types together.
9. Remove obsolete error-port links and metadata.
10. Update package major references, public API baselines, tests, and docs.

Do not add compatibility aliases that recreate the removed architecture. Native
language unions remain deferred until a stable feature is available across all
supported target frameworks.
