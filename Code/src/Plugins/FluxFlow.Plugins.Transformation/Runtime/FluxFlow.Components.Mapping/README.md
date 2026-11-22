# FluxFlow.Components.Mapping

Mapper accepts a component-specific options object. Supply a mapping function;
the component has no engine, expression parser, JSONata dependency or registration
responsibility. Targets .NET 8 and .NET 10.

```csharp
using System.Text.Json;
using FluxFlow.Components.Mapping;
using FluxFlow.Core.Messaging;

await using var mapper = new Mapper(new MapperOptions
{
    MapAsync = (message, _) => ValueTask.FromResult<FlowValue>(
        new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value.Json.GetInt32() * 2)))
});
```

This function doubles a numeric input without an expression engine. In the repository,
`eng/mapping-consumer/Program.cs` shows the complete native Dataflow example, including
input, output, error recovery and completion. Run `./eng/verify-mapping-consumer.ps1`
from the repository root to verify it against locally packed packages on .NET 8 and 10.

MapAsync has type Func<FlowMessage, CancellationToken, ValueTask<FlowValue>>.
It receives the original message so setup-supplied functions can use headers and IDs;
payload-only functions read `message.Value`. This is a breaking signature change from
the earlier FlowValue-only delegate. The component knows nothing about expression engines.
The caller owns any captured engine and its disposal. Set Execution to native
ExecutionDataflowBlockOptions for parallelism/capacity/cancellation; the supplied
function must support the selected concurrency. Defaults use bounded serial input
and output (128). OutputCapacity bounds native broadcast admission, not downstream retention. Construct options
once before constructing the component; runtime options updates are not supported.

Each successful input produces one derived message using WithValue. Trace,
correlation and headers are preserved, with a new message ID and input causation.
Existing errors bypass mapping unchanged by default. Set `HandleErrors = true`
(or `"HandleErrors": true` in mapper configuration) to map error payloads too,
for example into log requests. This uses the same mapping function and preserves
message lineage. Conversion/function exceptions and null
results become processing errors through Core; requested shutdown uses cancellation.
JSON arrays remain one payload, not multiple messages. CLR values do not need JSON
materialization. Engine serialization and expression preparation remain external.

Wire Input and Output with native Dataflow links, or assign names using application
composition outside the component. Options contain executable code and are not a
direct JSON configuration schema. No common settings base is introduced.
