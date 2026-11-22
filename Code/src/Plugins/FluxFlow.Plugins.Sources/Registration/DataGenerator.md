# DataGenerator setup

Install the Sources plugin in Studio. For a code-first host, reference the Sources package and register the components:

```csharp
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Application;

services.AddScheduler();
services.AddDataGenerator();
services.AddFluxFlowApplication(configuration);
```

```json
{
  "Workflows": {
    "samples": {
      "clock": {
        "Type": "scheduler",
        "Interval": "00:00:05",
        "Output": "generate.Input"
      },
      "generate": {
        "Type": "dataGenerator",
        "Expression": "{'deviceId': 'sensor-01', 'temperature': $fakeDecimal(15, 35), 'sampledAt': $fakeRecent(1), 'requestId': $messageId}",
        "Locale": "en"
      }
    }
  }
}
```

Add an Output reference to connect the generated messages to a consumer. Input,
Output, Events and Error support the same typed references and conditions as other
components. Error selects error values on Output; it is not a separate runtime port.

The generator processes each incoming payload. Replace the Scheduler with any input
source to combine its fields with Bogus values, for example
`{'deviceId': deviceId, 'temperature': $fakeDecimal(15, 35)}`.

Expression is an ExpressionSource: plain/inline expressions, `.jsonata` files,
explicit file paths and Base64 text are supported. For example use
`"Expression": "./samples/reading.jsonata"`. Pass an expressionBaseDirectory to
AddDataGenerator to select the file root; the default is AppContext.BaseDirectory.
Files are read and expressions parsed once at Build, not during registration or per
message. Invalid locales are rejected during configuration validation; syntax/file
failures identify the component at Build. Runtime errors follow the normal processing-error path.

Studio previews inline and Base64 expressions through the plugin's existing evaluator,
using the selected locale and isolated sample input. File access remains deferred to
the runner. The preview does not start a workflow or change the saved expression.

Locale defaults to `en`; OutputCapacity defaults to 128. There is no Seed, Interval
or Engine setting. JSONata and Bogus are part of this component, and no separate
expression-engine registration is required. Conditional links still use the normal
host-registered expression engines independently.

The built-in functions include dates, times, durations, names, contact details,
locations, text and product data. For example, `$fakeDate('2025-01-01', '2025-12-31')`
returns an ISO date and `$fakeTime('08:00', '16:00')` returns a time of day. Generation
uses the same expression and Locale settings; no additional setup is required.

See the [runtime library README](../Runtime/FluxFlow.Components.DataGeneration/README.md) for the supported `$fake...` functions and message
bindings. The ordinary Mapper and its JSONata engine have no Bogus functions added.
