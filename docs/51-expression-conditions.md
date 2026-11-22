# Configurable conditions

Select an expression engine where the expression is declared. Mapper and Router have
their own Engine setting. A conditional link declares Engine beside Condition:

```json
{
  "Workflows": {
    "Main": {
      "Source": {
        "Type": "source",
        "Output": {
          "Address": "Sink.Input",
          "Condition": "value.amount > 10",
          "Engine": "csharp"
        }
      },
      "Sink": { "Type": "sink" }
    }
  }
}
```

The host must register the component types, ports and engine used by its document.
Native C# predicates through LinkFactory/ports.Link need no expression engine.

Engine is a string key for a standard keyed IFlowExpressionEngine service. This allows
custom engines and different configured instances of one engine without extending an enum
or adding a catalog. Explicit keys match DI keys exactly in Mapper, Router and links;
they are not trimmed or lowercased. The bundled keys are `csharp` and `jsonata`.
Update earlier uppercase Mapper/Router engine spellings to the registered key.

Application and workflow sections do not select expression engines.
Move old workflow/application engine choices onto each conditional link. A link declared
from Input follows its own Engine exactly as a link declared from Output; crossing workflow
boundaries never changes its engine. A condition without Engine uses the host's unkeyed
default engine. An unknown explicit key fails compilation and never falls back.

Engine must be a non-empty string and requires Condition. Put constant values directly
in the expression. The former
Parameters field is rejected during registration; substitute those values into Condition
when migrating an existing configuration.

A false condition filters the message from that link. All matching links receive the
message; if none match, it is consumed without delivery or an error. The former Fallback
field and LinkFallback API are removed. A thrown expression is still an error, not false.

Inputs follow the shared message contract: value, headers, string messageId/traceId/
correlationId/causationId, timestamp, plus the existing input/payload/message aliases.
C# exposes lazy JSON dot access and DateTimeOffset; JSONata has equivalent bindings and
ISO timestamp text. Conditions must return bool, without truthiness coercion.

Setup captures link declarations during Add and compiles during Build. Engines resolve
through native DI. Compiled conditions and compilation failures are reused within the
provider's registration session, keyed by engine and expression. Message evaluations are not cached. Different
engines on two links in the same workflow remain independent. Link expression text is inline;
Mapper/Router expression-file support belongs to their adapters.

Invalid link syntax fails during registration with a component/port location.
Unknown engines and invalid expressions fail Build before sources start.
No separate document diagnostic API, hot reload or automatic engine selection
by workflow is included.
