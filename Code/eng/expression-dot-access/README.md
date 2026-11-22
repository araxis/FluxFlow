# Expression dot-access experiment

Run independently of the production solution:

```powershell
dotnet run --project eng/expression-dot-access/ExpressionDotAccess.csproj -c Release -f net10.0
dotnet run --project eng/expression-dot-access/ExpressionDotAccess.csproj -c Release -f net8.0
```

Uses DynamicExpresso.Core 2.19.3 and the real Core FlowValue via a one-way
project reference. No production project depends on this experiment; it is not
packaged or included in FluxFlow.sln. No component/options API changed.

Compares a compiled predicate using a read-only DynamicObject view of FlowValue.Json
against a recursively populated ExpandoObject. A hand-written JsonElement predicate
is a lower-level reference, not an interchangeable dynamic expression engine.
Headers use a small ExpandoObject populated from each header FlowValue.Json.
Expression parameters are separate value, headers and string messageId values.

Checks nested properties, array indexing, preserved strings/numbers/bool/null,
non-identifier key indexing on the view, missing-field errors, concurrent evaluation,
owned JSON after source-document disposal and FlowValue's once-only JSON generation.
The experiment compiles once, creates adapters per evaluation, and never sets
per-message interpreter variables. The interpreter is initialized before parallel use.

Measurement includes adaptation, evaluation and allocations on the calling thread;
fresh-value cases also include synthetic payload construction/serialization and
FlowValue's existing owned JSON clone. Cached cases reuse FlowValue.Json.
Compilation, input transport, output mapping/serialization, Dataflow scheduling,
GC pause latency, and workflow throughput are not measured. Five rounds of 10,000
evaluations follow 3,000 warmups. Cases run in a fixed order in one process:
this is a feasibility probe, not a statistically controlled BenchmarkDotNet suite.
Tiering/GC/machine noise can reverse small differences; do not infer cold view is
faster than direct JSON access. Payloads deliberately include unused fields, which
favors lazy access and represents sparse predicates, not whole-document mapping.

Prototype limitations: exact-case JSON names; missing fields throw; numeric conversion
uses Int64 then Decimal then Double. This policy is not yet a public contract.
No automatic date/number parsing of JSON strings. Array enumeration/LINQ, mutation,
arbitrary CLR members, comprehensive numeric edge cases and dynamic mapper output
conversion are not implemented/qualified. Non-identifier keys use indexers; dot
syntax cannot support every JSON key. Expando does not itself provide the same
string indexer, so that test is deliberately view-only.

Recommendation: use the existing JSON representation as the shared dynamic input
boundary; take this small view into the C# engine integration only after mapper
output/numeric semantics are covered. Keep it outside Core and components.
Do not add an eager dictionary tree or a general reflection adapter by default.
