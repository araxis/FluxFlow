# Expression dot access: feasibility and cost

No production API or component implementation changed. The isolated executable in
`eng/expression-dot-access` exercises the actual FlowValue.Json cache with
DynamicExpresso.Core 2.19.3. It is not part of the main solution or a shipped package.

Branch/head verified: codex/core-only-component-redesign,
6b85b45f72cf8ccaec27579e340799c1118aa3c2. The pre-existing modified tree was preserved.
The registration-closeout evidence still records 2,581 passing tests; those are
prior results, not newly rerun production tests in this experiment.

## Result

Nested dot access works with a small read-only DynamicObject view of JsonElement.
Headers obtain JSON from their existing FlowValue objects; no blanket reserialization
or recursive copy is necessary. FlowValue generates JSON once even with concurrent
readers. Compiled delegate evaluation passed concurrent checks with per-call adapters.
Missing fields remain errors; null and numeric-looking strings retain their meanings.

Fresh Release probe on .NET 10.0.12 (32 logical CPUs), median of five rounds,
10,000 calls each, microseconds and allocated bytes per evaluation:

| Payload / input | Direct JSON | Dynamic JSON view | Eager Expando |
| --- | ---: | ---: | ---: |
| 4 unused fields, cached | 0.510 / 64 B | 0.950 / 576 B | 3.791 / 3,608 B |
| 100 unused fields, cached | 0.380 / 64 B | 0.704 / 576 B | 46.456 / 53,785 B |
| 4 unused fields, fresh typed value | 6.023 / 2,208 B | 6.592 / 2,720 B | 8.630 / 5,848 B |
| 100 unused fields, fresh typed value | 17.203 / 23,816 B | 16.575 / 24,328 B | 59.704 / 77,686 B |

The fresh-value measurements include constructing/serializing the synthetic payload.
Small timing differences are noise-sensitive (notably the last row's direct/view
ordering). The dynamic view still allocates wrappers and boxed scalars; dot access
is not free. Sparse field access favors the lazy view. These numbers are not mapper
throughput, JSONata comparisons, distributed load results or GC pause measurements.
The experiment also runs on .NET 8; raw outputs are in
`artifacts/expression-dot-access-20260920/net8.log` and `net10.log`.

## Decision and next slice

Dot access is feasible with modest local complexity and a much smaller allocation
cost than eager conversion in this workload. Use FlowValue.Json as the dynamic data
boundary; do not put engine-specific behavior in FlowValue or components.
Keep the view read-only, parameters separate, compilation once, invocation data local.
Keys that are not C# identifiers require indexers. Dynamic field existence cannot be
fully validated before receiving runtime data.

The next implementation slice is the external Mapper setup/engine integration:
first carry the original message to the supplied mapping delegate so setup can
provide headers and string IDs; then support compiled expression input/output and
inline/file resolution at that boundary. Preserve WithValue lineage and error handling.
Qualify numeric rules and mapper output conversion before promoting the prototype.
No custom catalog, activation context, generic reflection tree, or engine dependency
in Core/Mapper is justified by this result.
