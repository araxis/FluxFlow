# Router

Routes messages to named predicate outputs. Configuration type: `router`.

## Setup

Set **Engine** and define **Routes** as an object of output names and expressions:

```json
{
  "Hot": "Reading > 30",
  "Cold": "Reading < 10"
}
```

Each route adds a canvas output port. Changing a route name does not rewrite existing
links; update those links explicitly. Built-in port names cannot be used as route names.

## Ports

**Input** receives messages. Every matching named output receives the original message;
this is not first-match routing. **Unmatched** receives ordinary messages matching no route.
**Output** carries errors, including incoming errors that bypass route expressions.
**Error** is a filtered view of that Output; **Events** is the component event port.
`OutputCapacity` defaults to 128.
