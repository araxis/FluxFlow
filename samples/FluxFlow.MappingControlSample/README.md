# FluxFlow Mapping And Control Sample

This console sample shows how a host can compose reusable mapping, control, and
assertion components while keeping source, sink, and domain-specific context
code inside the application.

The flow is:

```text
source -> flow.mapper -> flow.filter -> flow.when -> priority / standard sinks
                         |
                         `-> flow.assert -> assertion sink
```

The sample registers one small expression engine and two context factories:

- `IncomingOrderContextFactory` exposes variables for `flow.mapper`.
- `ReviewedOrderContextFactory` exposes variables for `flow.filter`,
  `flow.when`, and `flow.assert`.

Run it from the repository root:

```sh
dotnet build samples/FluxFlow.MappingControlSample/FluxFlow.MappingControlSample.csproj /nr:false
dotnet run --project samples/FluxFlow.MappingControlSample/FluxFlow.MappingControlSample.csproj --no-build
```

Expected shape:

```text
Sample: mapping-control
Stored orders: 3
priority: A-100 Harbor Market total=125.00 priority=True
standard: A-101 Cedar Supply total=42.00 priority=False
priority: A-102 Summit Works total=230.00 priority=True

Assertions: 3 passed, 0 failed
Diagnostics observed: 14
```
