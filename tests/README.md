# Test architecture

Projects live directly under their test kind, matching the Visual Studio solution:

    tests/
      Unit/<project>
      Integration/<project>
      Support/<shared-test-library>

Acceptance and Smoke currently have no projects after the component reset. Future
projects of either kind should also live directly under their kind, without extra
product-area, container or scenario directory layers.

| Kind | Purpose | Dependencies | Default execution |
| --- | --- | --- | --- |
| Unit | One package or cohesive module | In-process substitutes only | Every build |
| Integration | Production adapters against disposable infrastructure | Test-owned containers and real sockets | CI and container-capable development |
| Acceptance | Cross-module product behavior through public definition APIs | Deterministic in-process boundaries | Every build |
| Smoke | Deployed or externally managed environment verification | Explicit endpoints and credentials | Opt-in after deployment or on schedule |

Support contains reusable test infrastructure and is not a fifth test kind.
Scenario-specific settings, containers, and probes stay inside their scenario
project. Shared support is promoted only after multiple scenarios need exactly
the same semantics.

## Naming

- Projects use .Tests, IntegrationTests, .Acceptance.Tests, or SmokeTests.
- Test classes name the subject or scenario and end in Tests.
- Test methods describe behavior using Condition_expected_behavior.
- Category traits are Acceptance, ContainerIntegration, and ExternalSmoke.
- Tests allocate unique ports, topics, queues, subjects, databases, and client
  identifiers. They never depend on execution order or shared external state.

## Running

Run ./eng/test.ps1 with Suite set to Unit, Acceptance, Integration, Smoke, or
All. All excludes external smoke tests. Add IncludeLicensedDatabaseTests when
the database container license has been accepted.
