# Remove intermediate document definitions

The user explicitly removed the entire intermediate document model and its dependent
path. This supersedes the previous decision to retain explicit document APIs and the
temporary deferral of DocumentValidation.

## Removed

- ApplicationDefinition, WorkflowDefinition, ComponentDefinition, ResourceDefinition.
- DocumentValidation and CanonicalApplicationProperties, used only by those models
  and the former document parser.
- ApplicationDefinitionJson, its converter and ApplicationDefinitionConfiguration.
- FlowJson and the document link compiler, compiled-link result, projections,
  declaration-side enum and located diagnostic types.
- Document-only serialization/projection tests, the document construction fixture,
  LinkCompilerFixture and the document compiler benchmark.

No public compatibility wrappers or replacement document hierarchy were added.
The API removals are intentional breaking preview changes. Message/value serialization
and component-specific configuration DTOs are unaffected.

## Remaining setup

IConfigurationSection is the hierarchy. Each registered component/resource callback
binds its own DTO and registers deferred native DI factories and selectors. A component's
input/output properties are parsed in the same section loop and register LinkFactory
delegates directly. Build resolves them through the host provider.

The existing condition compiler and parameter validator moved from Core to Configuration.
The former shared parser is now an internal per-port LinkConfiguration helper; document
traversal, declaration serialization and projection objects were deleted. Its private
per-link parsed settings are local to registration, not an application model or registry.
The helper returns the actual LinkFactory delegates. Address checks remain in
ApplicationAddress and runtime routing retains native validation.

Input/output directions, mixed arrays, named ports, relative/full addresses, Engine,
Parameters, conditions, fallback/delivery policies, duplicate conflict checks, registration
atomicity and compile-session caching are retained. Unknown explicit engines cannot
fall back. UI persistence and document editing are outside this scope.

## Verification

Runtime parity scenarios now use the real IConfiguration registration path in the
Configuration tests. Native delivery tests retain fallback exclusivity, condition
failures, rejection-handler isolation, bounded backpressure and cleanup coverage.
New section tests check both declaration sides, default/keyed engines, distinct bound
parameter sets, registration snapshots, missing engines and malformed declarations.
Tests solely for the removed serializer and document diagnostic API were deleted;
their count is not interpreted as runtime coverage.

Release build completed with zero warnings/errors. API baseline updated for the
intentional removals. Isolated NuGet consumers passed on .NET 8 and .NET 10:

- artifacts/cleanup-review-20260920/no-definition-core-consumer.log
- artifacts/cleanup-review-20260920/no-definition-configuration-consumer.log

Final full-solution test run passed all 1,784 executions. Configuration no longer
needs friend access to Core internals; that assembly grant is also removed and
the dependent project build verifies the boundary.
Final full-solution test evidence:
artifacts/cleanup-review-20260920/no-definition-verified-tests.log.
Earlier migration logs contain resolved intermediate build/test failures and are not
the final acceptance result. No commit or publication.
