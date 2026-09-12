# Portable resource boundary

The resource capability is implemented outside Core. The Json README and direct
entry-point error incorrectly described it as an unfinished migration and implied
that resource documents required the hosted update runtime. Corrected both without
adding a resource container, creator abstraction, dependency or public API.

## Supported paths

| Need | Entry point and ownership |
| --- | --- |
| Direct instances | Add constructed components to a Core workflow; no document binding required |
| Portable document with host-supplied dependencies | FlowJson.BuildAsync(document, bindings); host supplies dependencies and keeps their ownership; Resources is empty |
| Portable document declaring resources | Optional Composition document.BuildApplicationAsync(applicationServices); application scope owns created resources, parent host services remain borrowed |
| Live updates | Optional Engine; not required for either ordinary document-construction path |

The flat JSON schema requires both Resources and Workflows. The direct path uses
`"Resources": {}`; removing the property is not a valid alternative. The final
error explicitly says to leave Resources empty and points to the services overload.

Composition references Core, Json and Mapping, not Engine or Designer. Its existing
ContractApplicationExtensions compiles and validates declared contracts, registers
application resources, constructs FlowApplication, adds actual components and links
Core endpoints. It does not construct the retired composition runtime.

## Concrete preservation evidence

Contract_graph_executes_with_owned_resources_borrowed_host_and_native_ports has
two cases: embedded contracts and a serialized/deserialized resource document with
registered contracts. It asserts exactly one component construction, registration
and owned resource creation, exact native port identity, the transformed reply
`prefix:configured:value`, zero pending exchanges and disposal order
node/activation/resource. The borrowed host service survives application disposal
and is disposed only by the host provider.

The same suite checks invalid metadata before registration/activation, partial
activation/start failure cleanup, cancellation and duplicate ownership. Its named
condition case asserts one compilation, four evaluations, no use of the unselected
engine, exact accepted values 6 and 8 and one isolated rejection retaining the
original failure. ApplicationConditionCompiler owns resolution/cache state per
compilation session and selects the source workflow's engine before application
defaults. This is scoped evidence, not complete adapter/export/telemetry approval.

The new Resource_documents_require_composition_before_any_binding_runs theory
crosses string/document entry points with one/two resources. It requires the
specific exception, zero binding invocations, the composition overload guidance
and the explicit-host/empty-resource alternative. No transport or resource is
constructed by this test. Existing empty-resource document execution remains covered.

## Validation boundary

The metadata-free direct bindings path parses the model and checks unknown types
before invoking bindings. Actual native port existence is checked after obtaining
the component's port map; failed construction cleans up newly owned instances and
does not start sources. This is not a claim of pre-constructor validation for every
semantic graph error. The optional contract path has metadata for broader preflight.
Constructors must not perform transport I/O. No duplicate port schema or speculative
context-aware Add overload was introduced to conceal this distinction.

## Verification artifacts

Initial narrow runs passed Json 112 and direct contracts 31 after four observed red
cases on the old error message. The first package attempt was superseded before
consumer acceptance when wording review caught the required Resources property.
Its frozen feed remains under local-candidate-20260911-resource with an explicit
disposition. The final text and test expectation say to leave Resources empty.

The current source qualification is resource-boundary-final-20260911; its verifier
requires a clean nonincremental Release build, all 81 local test reports, all four
new cases in execution and solution discovery, and unchanged source hashes. The
local candidate target is local-candidate-20260911-resource-final, with version
0.0.0-local.20260911.resource.2. Its completed acceptance.json verifies:

- 4,843 passing local tests in 81 reports; 5,053 discovered, including all four new
  cases; clean full Release build and successful fluent sample.
- One additional authored acceptance case, using supplied transport data and an
  in-process handler, not a real broker connection.
- 81 packages, 81 symbol archives, 81 matching readmes and 162 qualified target
  assemblies matching the copies in full-package consumers.
- Ten all-package consumer commands, including workflow/resources and independent
  durable restart processes, plus twelve commands for four minimal Core/Fluent
  consumers; all pass across net8.0 and net10.0 with isolated caches.
- 1,487 stable qualification inputs; only the two Json target DLLs differ from
  candidate t1430. Its runtime binaries are unchanged.

The assertion/gap review is .testagent/resource-boundary-review.md. No runtime
behavior or public signature was changed beyond the diagnostic text. Broader
capability/member migration, performance, telemetry and real integrations remain
separate goal gates. Nothing is published and no external consumer task is contacted.
