# Minimum framework baseline

The user replaced the nine-package consolidation target with a stricter boundary:
only FluxFlow.Core remains; optional modules will be added one at a time.

Removed production packages:

- FluxFlow.Expressions.CSharp and FluxFlow.Expressions.Jsonata
- FluxFlow.Persistence, FluxFlow.Persistence.Sqlite and FluxFlow.Persistence.SqlServer
- FluxFlow.Hosting (including its health checks)
- FluxFlow.Observability.OpenTelemetry and FluxFlow.Observability.AspNetCore

Their dedicated test projects, SQL Server runners, two expression benchmarks,
module consumer fixtures and module-specific release assertions are removed.
The package manifest and CI now target Core. A fresh package-only Core consumer
replaces the former multi-module persistence/health acceptance fixture.

Core production algorithms and public declarations are retained. Fourteen source
files are relocated into Application, Ports, Execution and Coordination. The old
DocumentBindingAccess friend declaration for the already-retired FluxFlow.Json
assembly is deleted. Public baseline hashes change because source paths and the
package inventory change; this step does not rename Core APIs or namespaces.

Core tests keep their routing, error, parameter, revision and fallback scenarios.
Where they previously required the C# engine, a test-only named-callback fixture
now implements the engine contract. It is not a parser or production engine.
Language-engine coverage goes away with the language modules, not with Core.

The layout has one production package and eight solution projects. Visual Studio
groups match src, tests/Unit, tests/Support and benchmarks. Core's physical folders
group application ownership, endpoints, messaging, processing, shared execution,
coordination, configuration, revisions, mapping contracts and resilience.

This is a reduced module baseline, not a claim that everything inside Core is
irreducible. JSON construction, transactional revisions, capture contracts and
native diagnostic hooks still exist. Provider implementations, language parsers,
exporters, HTTP endpoints and external host adapters do not.

## Recovery and validation

Before changes, Git connectivity was checked and the modified tree was archived
to `artifacts/minimum-core-20260915/before-minimum.zip`; ZIP integrity passed.
The archive preserves newer uncommitted work that is not on main. It is ignored
local evidence, not a dependency or a remotely stored backup.

Validation completed:

- Release build: zero warnings and errors.
- Full retained solution regression: 2,381 passed in seven reports, no failures
  or skips. Core's 522 tests per framework, Nodes' 323 per framework, 536 runtime
  tests and 45 coordination tests remain. The reduced total reflects removed
  module suites and their release checks; 110 Core release checks remain.
- Fresh package-only consumer: passed on .NET 8.0.31 and .NET 10.0.12. Direct and
  JSON construction, 16 concurrent correlated requests per graph, error replies,
  continued processing and graceful stop ran using only the new Core package.
- Dependency inspection: the consumer resolved only FluxFlow.Core among FluxFlow
  packages; the package has no dependency on another FluxFlow package.
- Source comparison: all 206 retained production C# files match the pre-removal
  bytes, including the 14 relocated files. Only the obsolete assembly friend
  declaration is removed from Core.
- Removed optional production source: 73 C# files / 16,031 physical lines. This
  measures module removal, not an equivalent reduction in implementation size
  while retaining those optional capabilities.
- Solution/project-reference integrity and whitespace checks passed.

Evidence: `artifacts/minimum-core-20260915/verification.json`, `build.log`,
`regression.log`, `regression/` and `consumer-3/`. The consumer candidate is
`0.0.0-local.20260915053214`. Earlier failed setup/test attempts remain separate;
their results are not included in the final totals. Historical 3,436-test /
nine-package results describe the previous baseline, not this one.
No commit, push, publication, external service or UI project change is included.

Next: review the Core baseline, then choose and design the first component/module.
