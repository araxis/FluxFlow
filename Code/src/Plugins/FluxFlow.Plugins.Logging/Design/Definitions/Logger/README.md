# Logger

Writes log requests through the configured logger and emits their processing results.
Configuration type: `logger`.

## Setup

**Logger resource** refers to a logger supplied by the host. **Category** identifies
the logging category. Choose log destinations and capture policy in the host;
this component does not provision a logging server.

## Ports

**Input** accepts a log request or an array of requests, not an arbitrary payload:

```json
{
  "LogLevel": "Information",
  "MessageTemplate": "Reading {Value}",
  "Args": [21.5]
}
```

**Output** emits each processed request. **Events** is the component event port;
**Error** is filtered Output. Incoming error messages pass through without being logged.
`OutputCapacity` defaults to 128.

For error logging, link a component's filtered Error output to a Mapper with
**Handle errors** enabled, convert it to a log request, then send it to Logger.
Runtime log display requires an attached runner and collector.
