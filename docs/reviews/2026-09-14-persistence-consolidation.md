# Persistence consolidation

The six input/output packages now have three owners: FluxFlow.Persistence,
FluxFlow.Persistence.Sqlite and FluxFlow.Persistence.SqlServer. Each keeps Input
and Output folders. Production project count falls from 14 to 11; the solution
falls from 35 to 29 projects because six unit test projects also became three.
The two external SQL Server integration projects and their runners remain.

All 62 production C# files are byte-identical to the pre-move snapshot. This step
simplifies project navigation, dependency references and package selection. It
does not claim to reduce implementation complexity, improve performance or
change delivery, retry, lease, dead-letter or recovery behavior.

Existing namespaces are retained. Consumers must replace old durable package
references with the corresponding new provider package and rebuild: assembly
identity changed, and no forwarding assemblies were added. New package IDs use
1.0.0-alpha.1 development versions; nothing was published.

Release build passed with zero warnings/errors. The selected local regression
passed 3,429 tests in 19 reports, using the final Release and Persistence reports
in place of their initial failing runs. Three package-version cases disappeared
because six package versions became three; behavior coverage was retained.
All 11 packages packed, including provider documentation. An isolated net8.0
package-only consumer verified candidate identities and SQLite input/output
recovery across process restart. The first attempt under the repository inherited
central package management; qualification used a fresh external temporary folder.
Live SQL Server integration was not executed. SQL Server unit tests passed on
net8.0/net10.0; other suites retain their existing target frameworks.

Evidence: artifacts/persistence-consolidation-20260914/verification.json,
build-final.log, consumer-qualified.log and the referenced TRX reports.

Next: inspect Core/Engine ownership and execution differences before consolidation.
Revisit HealthChecks/Hosting after that boundary is settled. Nine production
projects is the initial target, conditional on preserving meaningful boundaries.
