# Release-Candidate Consolidation

This document records the supported authoring paths and the validation evidence
for the consolidated FluxFlow release candidate. It is a release-readiness
record, not a publication announcement. No package was published and no tag or
release was created.

## Supported authoring paths

FluxFlow intentionally supports two independent ways to define an application.
They converge on the same canonical engine, but they serve different use cases.

### Typed C# code-first

Code-first applications use complete component and resource contracts. The
application definition retains the executable descriptors that it needs, so a
normal host registers the finished definition once:

```csharp
var builder = new ApplicationDefinitionBuilder();
builder.AddWorkflow("main", out var workflow);

workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("upper", SampleComponents.Uppercase, out var upper);

source.Output.ConnectTo(upper.Input);

var definition = builder.Build();
services.AddFluxFlow(definition);
```

The shortened example emphasizes the ownership rule: the definition owns the
contracts selected while authoring. Normal code-first use does not repeat those
component or resource registrations in the service-registration section.
Typed handles remain available for sending, receiving, observing, durable
input, and durable-output capture.

C# predicates and other executable delegates are allowed on this path. A
code-first definition is an in-process executable blueprint; it is not required
to serialize to the portable JSON format.

### Portable JSON

Portable JSON remains a separate, data-only source for configuration files,
designers, persistence, and hot reload. It contains portable application data,
not executable C# delegates or embedded runtime factories.

A JSON host deserializes the definition, explicitly registers the component and
resource packages that may be named by that data, and registers the resulting
definition with FluxFlow. The package catalog is intentionally explicit because
untrusted configuration must not cause reflection-based discovery or magic
activation.

The package-only acceptance fixture proves that a JSON definition can start and
route by address, that applying the same definition is `Unchanged`, and that an
independently deserialized invalid candidate is `Rejected` without replacing
the active revision. A fresh message routes successfully after that rejection.

## Consolidation boundaries

This round did not add a parallel runtime, a new workflow feature, reflection,
assembly scanning, a background worker, polling, or a new dependency. It froze
the accepted typed C# and portable JSON paths and consolidated their release
evidence.

All 19 component-family package READMEs now state the same boundary:

- complete contracts own their executable descriptors;
- normal code-first applications call `AddFluxFlow(definition)` without
  repeating ordinary family registration;
- explicit family registration remains available for JSON, configuration,
  catalog, and advanced dynamic scenarios.

## Exact candidate

- Branch: `work/release-candidate-consolidation`
- Candidate commit: `4bf69015b9d3eaa95a45630c91d378c45c5a2aaa`
- Baseline commit: `37c1de35e335d84541ec06ad9f388cfce1b55876`
- Detached validation worktree:
  `C:\Users\meisa\AppData\Local\Temp\fluxflow-rc-4bf69015`

The detached worktree was created from the exact candidate commit. Restore,
build, tests, packaging, formatting, dependency audit, and provider validation
therefore did not depend on untracked source or stale output from the primary
working tree.

## Validation evidence

| Requirement | Evidence |
| --- | --- |
| Clean restore | 137 projects restored successfully. |
| CI-style Release build | 137 projects built with `ContinuousIntegrationBuild=true`; 0 warnings and 0 errors. |
| Complete solution tests | 2,675 passed across 67 test projects; 0 failed and 0 warnings. |
| Complete release-governance tests | 191 passed; 0 failed and 0 warnings. |
| Public API freeze | `PublicApiBaselineTests`: 2 passed; 0 failed and 0 warnings. |
| Formatting and whitespace | `dotnet format --verify-no-changes` exited 0; `git diff --check` exited 0. |
| Dependency security | Direct and transitive vulnerability audit reported no vulnerable packages for every solution project. |
| Packed-package consumer | Ten exact candidate packages were packed, hash-verified, restored, built, and executed from an isolated source. The fixture completed its default, seed, and recovery processes and cleaned both owned temporary directories. |
| JSON rollback | Package-only execution proved initial routing, `Unchanged` reapply, invalid-candidate `Rejected`, exact active-revision retention, and successful post-rejection routing. |
| Code-first and resources | Package-only execution emitted the exact code-first and executable-resource success markers without repeated host registration. |
| Optional capabilities | Package-only execution emitted the exact health, Fluent, durability, restart, idempotency, and overall completion markers once each. |
| Real durable input | T-SQL integration: 90 passed, 0 failed, 0 skipped. |
| Real durable output | T-SQL integration: 117 passed, 0 failed, 0 skipped. |
| Provider identity | Both suites used the same pinned provider-image digest, `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`. |
| Cleanup | Candidate package source, consumer work directory, SQL containers, and detached-worktree repository state were clean after their gates. |

The package-only gate performed 15 process invocations: ten package operations,
one restore, one build, and the default, seed, and recovery executions. Its
success markers covered Engine, code-first authoring, executable resources,
health, Fluent, durability, restart seeding and recovery, idempotency, restart
completion, and overall completion. Every required marker appeared exactly
once.

## Deliberately deferred

Publication remains a separate authorized action. This consolidation did not
push the branch, open a pull request, create a tag or release, or publish a
package. Provider-specific operational tuning, new component families, and new
workflow features remain future work and are not release blockers for this
candidate.
