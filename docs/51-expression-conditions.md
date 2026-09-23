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
          "Port": "Sink.Input",
          "Condition": "value.amount > minimum",
          "Engine": "csharp",
          "Parameters": { "minimum": 10 }
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
or adding a catalog. Explicit link keys match DI keys exactly. The bundled Mapper/Router
adapters normalize their own configured keys to lower case as documented in Configuration.

Application and workflow sections do not select expression engines.
Move old workflow/application engine choices onto each conditional link. A link declared
from Input follows its own Engine exactly as a link declared from Output; crossing workflow
boundaries never changes its engine. A condition without Engine uses the host's unkeyed
default engine. An unknown explicit key fails compilation and never falls back.

Engine must be a non-empty string and requires Condition. Fallback links cannot contain
a condition, engine or parameters. Parameters is an optional JSON object; input, payload
and message remain reserved parameter names. Values are copied and detached from caller
JSON lifetimes. Bound parameters retain their existing override behavior.

Inputs follow the shared message contract: value, headers, string messageId/traceId/
correlationId/causationId, timestamp, plus the existing input/payload/message aliases.
C# exposes lazy JSON dot access and DateTimeOffset; JSONata has equivalent bindings and
ISO timestamp text. Conditions must return bool, without truthiness coercion.

Setup captures link declarations during Add and compiles during Build. Engines and compiled
conditions are reused within the provider's compilation session; keys include engine,
expression, input type and bound parameters. Message evaluations are not cached. Different
engines on two links in the same workflow remain independent. Link expression text is inline;
Mapper/Router expression-file support belongs to their adapters.

Invalid link syntax fails during registration with a component/port location.
Unknown engines and invalid expressions fail Build before sources start.
No separate document diagnostic API, hot reload or automatic engine selection
by workflow is included.
