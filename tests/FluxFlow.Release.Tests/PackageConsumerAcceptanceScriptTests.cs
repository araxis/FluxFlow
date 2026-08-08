using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageConsumerAcceptanceScriptTests
{
    private const string FixtureDirectory = "package-consumer-acceptance";
    private const string FixtureProject = "FluxFlow.PackageConsumerAcceptance.csproj";

    private static readonly IReadOnlyDictionary<string, string> ExpectedFixturePackageVersions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FluxFlow.Engine"] = "$(FluxFlowEngineVersion)",
            ["FluxFlow.Engine.HealthChecks"] = "$(FluxFlowEngineHealthChecksVersion)",
            ["FluxFlow.Engine.DurableInput.SqlFile"] = "$(FluxFlowDurableInputSqlFileVersion)",
            ["FluxFlow.Engine.DurableOutput.SqlFile"] = "$(FluxFlowDurableOutputSqlFileVersion)",
            ["FluxFlow.Fluent"] = "$(FluxFlowFluentVersion)",
            ["Microsoft.Extensions.Hosting"] = "8.0.1"
        };

    private static readonly string[] RestartSeedMarkers =
    [
        "PACKAGE_ACCEPTANCE_RESTART_SEED_INPUT=restart-input",
        "PACKAGE_ACCEPTANCE_RESTART_SEED_OUTPUT=restart-preapplied-output",
        "PACKAGE_ACCEPTANCE_RESTART_SEED_OK=True"
    ];

    private static readonly string[] RestartRecoveryMarkers =
    [
        "PACKAGE_ACCEPTANCE_RESTART_INPUT_RECOVERED=True",
        "PACKAGE_ACCEPTANCE_RESTART_WORKFLOW_OUTPUT_CAPTURED=True",
        "PACKAGE_ACCEPTANCE_RESTART_PENDING_OUTPUT_RESUMED=True",
        "PACKAGE_ACCEPTANCE_RESTART_OUTPUT_RECOVERED=True",
        "PACKAGE_ACCEPTANCE_RESTART_IDEMPOTENCY_OK=True",
        "PACKAGE_ACCEPTANCE_RESTART_OK=True"
    ];

    private static readonly string[] RequiredAliases =
    [
        "nodes",
        "mapping",
        "composition",
        "engine",
        "engine-healthchecks",
        "fluent",
        "engine-durable-input",
        "engine-durable-input-sqlfile",
        "engine-durable-output",
        "engine-durable-output-sqlfile"
    ];

    [Fact]
    public void Acceptance_fixture_is_a_net8_package_only_consumer()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var projectPath = GetFixturePath(root, FixtureProject);

        File.Exists(projectPath).ShouldBeTrue();
        var project = XDocument.Load(projectPath);
        project.Descendants().Single(element => element.Name.LocalName == "TargetFramework")
            .Value.ShouldBe("net8.0");

        var packageVersions = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        packageVersions.ShouldBe(ExpectedFixturePackageVersions);
        project.Descendants().ShouldNotContain(element => element.Name.LocalName == "ProjectReference");

        var projectText = File.ReadAllText(projectPath);
        projectText.ShouldNotContain("..\\");
        projectText.ShouldNotContain("../");
        projectText.ShouldNotContain("/src/");
        projectText.ShouldNotContain("\\src\\");
    }

    [Fact]
    public void Acceptance_fixture_exercises_independent_json_complete_code_first_and_sql_file_reopen_paths()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(GetFixturePath(root, "Program.cs"));

        CountOccurrences(source, "PACKAGE_ACCEPTANCE_ENGINE_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_CODE_FIRST_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_RESOURCE_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_HEALTH_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_FLUENT_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_DURABILITY_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_OK=True").ShouldBe(1);
        source.ShouldNotContain("System.Reflection");
        source.ShouldNotContain("Assembly.Load(");
        source.ShouldNotContain("GetAssemblies(");

        source.ShouldContain("ApplicationDefinitionJson.Deserialize(definitionJson)");
        source.ShouldContain("services.AddFluxFlow(");
        source.ShouldContain("GetRequiredService<FluxFlowApplication>()");
        source.ShouldContain("Ensure(started.IsApplied");
        source.IndexOf("Ports.ReceiveAsync<string>", StringComparison.Ordinal).ShouldBeLessThan(
            source.IndexOf("Ports.SendAsync(", StringComparison.Ordinal));
        source.ShouldContain("PortSendStatus.Accepted");
        source.ShouldContain("PortReceiveStatus.Received");
        source.ShouldContain("PACKAGE-JSON");

        source.ShouldContain("RunCodeFirstEngineScenarioAsync()");
        source.ShouldContain("new ApplicationDefinitionBuilder()");
        source.ShouldContain(".AddWorkflow(\"Acceptance\", out var workflow)");
        CountOccurrences(source, "AcceptanceComponents.Uppercase").ShouldBe(2);
        CountOccurrences(source, "AcceptanceComponents.PrefixedUppercase").ShouldBe(1);
        source.ShouldContain("first.Output.ConnectTo(");
        source.ShouldContain("when: static value =>");
        source.ShouldContain("definitionBuilder.Build()");
        var codeFirstScenario = ExtractSourceSection(
            source,
            "static async Task RunCodeFirstEngineScenarioAsync()",
            "static async Task RunDurabilityScenarioAsync()");
        codeFirstScenario.ShouldContain("Ports.SendAsync(");
        codeFirstScenario.ShouldContain("first.Input,");
        codeFirstScenario.ShouldContain("Ports.ReceiveAsync(");
        codeFirstScenario.ShouldContain("second.Output,");
        codeFirstScenario.ShouldNotContain("first.Input.Address");
        codeFirstScenario.ShouldNotContain("second.Output.Address");
        codeFirstScenario.ShouldContain(
            ".AddResource(\"Prefix\", AcceptanceResources.Prefix, out var prefix)");
        codeFirstScenario.ShouldContain("definitionBuilder.Build()");
        codeFirstScenario.ShouldNotContain("AddApplicationResourceRegistrar");
        source.ShouldContain("ApplicationResourceContract<AcceptanceResourceHandle>");
        source.ShouldContain("ApplicationResourceContract.Create(");
        source.ShouldContain(": AuthoredResourceHandle(definition)");
        source.ShouldContain("AcceptanceResourceRegistrar : IApplicationResourceRegistrar");
        source.ShouldContain("context.Services.AddSingleton(new AcceptancePrefix(\"RESOURCE-\"))");
        source.ShouldContain("context.Services.GetRequiredService<AcceptancePrefix>()");
        source.ShouldContain("RESOURCE-PACKAGE-CODE");
        source.ShouldContain("ComponentContract<UppercaseComponentHandle>");
        source.ShouldContain("ComponentContract.Create(");
        source.ShouldContain("class UppercaseComponentHandle(ComponentHandle definition)");
        source.ShouldContain(": AuthoredComponentHandle(definition)");
        source.ShouldContain("OutputPortHandle<ComponentEvent> Events");
        source.ShouldNotContain("ComponentAuthoringContract");
        source.ShouldNotContain(".AddRuntimeComponent(");

        var jsonScenario = ExtractSourceSection(
            source,
            "static async Task RunEngineScenarioAsync()",
            "static async Task RunFluentScenarioAsync()");
        jsonScenario.ShouldContain("var definition = ApplicationDefinitionJson.Deserialize(definitionJson);");
        jsonScenario.ShouldContain("services.AddFluxFlowComponents()");
        jsonScenario.ShouldContain(".AddComponent(AcceptanceComponents.Uppercase)");
        jsonScenario.ShouldContain(
            "var unchanged = await application.ApplyAsync(\"package-json-unchanged\", definition);");
        jsonScenario.ShouldContain(
            "var rejected = await application.ApplyAsync(\"package-json-invalid\", invalidDefinition);");
        jsonScenario.ShouldContain("PACKAGE-JSON-AFTER-REJECTION");
        CountOccurrences(jsonScenario, "AcceptanceComponents.Uppercase").ShouldBe(1);
        codeFirstScenario.ShouldContain("definitionBuilder.Build()");
        codeFirstScenario.ShouldContain("services.AddFluxFlow(");
        codeFirstScenario.ShouldNotContain("AddFluxFlowComponents");
        CountOccurrences(codeFirstScenario, "AcceptanceComponents.Uppercase").ShouldBe(1);
        CountOccurrences(codeFirstScenario, "AcceptanceComponents.PrefixedUppercase").ShouldBe(1);

        source.ShouldContain("var graph = Flow");
        source.ShouldContain(".From(new SingleValueSource(");
        source.ShouldContain(".Then(new UppercaseNode())");
        source.ShouldContain(".To(new CollectSink(collector))");
        source.ShouldContain("await graph.StartAsync()");
        source.ShouldContain("await graph.Completion.WaitAsync(");
        source.ShouldContain("PACKAGE-FLUENT");

        source.ShouldContain("AddFluxFlowSqlFileDurableInput");
        source.ShouldContain("AddFluxFlowSqlFileDurableOutput");
        source.ShouldContain("GetRequiredService<IDurableInputStore>()");
        source.ShouldContain("GetRequiredService<IDurableOutputStore>()");
        source.ShouldContain("GetRequiredService<IDurableOutputDeliveryStore>()");
        source.ShouldContain("DurableInputEnqueueStatus.Enqueued");
        source.ShouldContain("DurableOutputEnqueueStatus.Enqueued");
        source.ShouldContain("DurableInputEnqueueStatus.AlreadyExists");
        source.ShouldContain("DurableOutputEnqueueStatus.AlreadyExists");
        source.IndexOf("await using (var writer", StringComparison.Ordinal).ShouldBeLessThan(
            source.IndexOf("await using (var reader", StringComparison.Ordinal));
        source.ShouldContain("inputStore.LeaseAsync(");
        source.ShouldContain("outputDelivery.TryLeaseAsync(");
        source.ShouldContain("inputLease.Envelope.Payload.GetRawText()");
        source.ShouldContain("persistedOutput.Envelope.Payload.GetRawText()");
    }

    [Fact]
    public void Acceptance_fixture_json_path_proves_unchanged_apply_and_rejected_candidate_without_losing_active_route()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(GetFixturePath(root, "Program.cs"));
        var jsonScenario = ExtractSourceSection(
            source,
            "static async Task RunEngineScenarioAsync()",
            "static async Task RunFluentScenarioAsync()");

        CountOccurrences(jsonScenario, "ApplicationDefinitionJson.Deserialize(").ShouldBe(2);
        jsonScenario.ShouldContain(
            "var definition = ApplicationDefinitionJson.Deserialize(definitionJson);");
        jsonScenario.ShouldContain("services.AddFluxFlow(");
        jsonScenario.ShouldContain("definition,");
        jsonScenario.ShouldContain("var activeRevision = application.Current;");
        jsonScenario.ShouldContain("var activeDefinition = application.CurrentDefinition;");
        jsonScenario.ShouldContain("ReferenceEquals(activeDefinition, definition)");

        jsonScenario.ShouldContain(
            "var unchanged = await application.ApplyAsync(\"package-json-unchanged\", definition);");
        jsonScenario.ShouldContain("unchanged.Status == ApplicationUpdateStatus.Unchanged");
        jsonScenario.ShouldContain("ReferenceEquals(unchanged.ActiveRevision, activeRevision)");

        jsonScenario.ShouldContain("\"Type\": \"acceptance.unavailable\"");
        jsonScenario.ShouldContain(
            "var invalidDefinition = ApplicationDefinitionJson.Deserialize(invalidDefinitionJson);");
        jsonScenario.ShouldContain(
            "var rejected = await application.ApplyAsync(\"package-json-invalid\", invalidDefinition);");
        jsonScenario.ShouldContain("rejected.Status == ApplicationUpdateStatus.Rejected");
        jsonScenario.ShouldContain("ReferenceEquals(rejected.ActiveRevision, activeRevision)");
        CountOccurrences(jsonScenario, "ReferenceEquals(application.Current, activeRevision)")
            .ShouldBe(2);
        CountOccurrences(jsonScenario, "ReferenceEquals(application.CurrentDefinition, activeDefinition)")
            .ShouldBe(2);

        jsonScenario.ShouldContain(
            "await AssertJsonRouteAsync(application, \"package-json\", \"PACKAGE-JSON\");");
        jsonScenario.ShouldContain("\"package-json-after-rejection\",");
        jsonScenario.ShouldContain("\"PACKAGE-JSON-AFTER-REJECTION\"");
        jsonScenario.ShouldContain("\"Acceptance.Uppercase.Input\"");
        jsonScenario.ShouldContain("\"Acceptance.Uppercase.Output\"");
        jsonScenario.ShouldContain("PortSendStatus.Accepted");
        jsonScenario.ShouldContain("PortReceiveStatus.Received");

        var rejectedIndex = jsonScenario.IndexOf(
            "var rejected = await application.ApplyAsync",
            StringComparison.Ordinal);
        var retainedRouteIndex = jsonScenario.IndexOf(
            "\"package-json-after-rejection\"",
            StringComparison.Ordinal);
        var stopIndex = jsonScenario.IndexOf("await application.StopAsync();", StringComparison.Ordinal);
        rejectedIndex.ShouldBeGreaterThanOrEqualTo(0);
        retainedRouteIndex.ShouldBeGreaterThan(rejectedIndex);
        stopIndex.ShouldBeGreaterThan(retainedRouteIndex);

        CountOccurrences(source, "PACKAGE_ACCEPTANCE_ENGINE_OK=True").ShouldBe(1);
        source.IndexOf("await RunEngineScenarioAsync();", StringComparison.Ordinal).ShouldBeLessThan(
            source.IndexOf(
                "Console.WriteLine(\"PACKAGE_ACCEPTANCE_ENGINE_OK=True\");",
                StringComparison.Ordinal));
        jsonScenario.ShouldNotContain("PACKAGE_ACCEPTANCE_ENGINE_OK=True");
    }

    [Fact]
    public void Acceptance_fixture_executes_optional_standard_health_readiness_from_packages_only()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(GetFixturePath(root, "Program.cs"));
        var project = File.ReadAllText(GetFixturePath(root, FixtureProject));

        project.ShouldContain(
            "<PackageReference Include=\"FluxFlow.Engine.HealthChecks\" Version=\"$(FluxFlowEngineHealthChecksVersion)\" />");
        project.ShouldContain("FluxFlowEngineHealthChecksVersion is required.");
        project.ShouldNotContain("ProjectReference");

        source.ShouldContain("using FluxFlow.Engine.HealthChecks;");
        source.ShouldContain("using Microsoft.Extensions.Diagnostics.HealthChecks;");
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_HEALTH_OK=True").ShouldBe(1);
        CountOccurrences(source, "services.AddHealthChecks()").ShouldBe(1);
        CountOccurrences(source, ".AddFluxFlowApplication();").ShouldBe(1);
        source.ShouldContain("GetRequiredService<HealthCheckService>()");
        source.ShouldContain(".CheckHealthAsync(static registration =>");
        source.ShouldContain("\"fluxflow.application\"");
        source.ShouldContain("health.Status == HealthStatus.Healthy");
        source.ShouldContain("health.Entries.Count == 1");
        source.ShouldContain("healthEntry.Tags.Order(StringComparer.Ordinal).SequenceEqual([\"fluxflow\", \"ready\"])");
        source.ShouldContain("healthEntry.Data[\"activeRevisionId\"] as string");
        source.ShouldContain("application.Current?.RevisionId");
        source.ShouldNotContain("class AcceptanceHealthCheck");
        source.ShouldNotContain(": IHealthCheck");
        source.ShouldNotContain("BackgroundService");
        source.ShouldNotContain("PeriodicTimer");
        source.ShouldNotContain("System.Reflection");
    }

    [Fact]
    public void Acceptance_restart_fixture_uses_explicit_hosted_recovery_and_idempotent_receipts()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var fixturePath = Path.Combine(root, "eng", FixtureDirectory);
        var program = File.ReadAllText(Path.Combine(fixturePath, "Program.cs"));
        var restart = File.ReadAllText(Path.Combine(fixturePath, "RestartDurabilityScenario.cs"));

        Directory.GetFiles(fixturePath, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ShouldBe(["Program.cs", "RestartDurabilityScenario.cs"]);

        CountOccurrences(program, "durability-restart-seed").ShouldBe(1);
        CountOccurrences(program, "durability-restart-recover").ShouldBe(1);
        program.ShouldContain("args.Length != 2");
        program.ShouldContain("Path.IsPathFullyQualified(args[1])");
        program.ShouldContain("if (args.Length > 0)");
        program.ShouldContain("default:");
        program.ShouldContain("Unknown package-consumer acceptance mode");
        program.ShouldContain("Restart durability modes require exactly one absolute data-directory argument.");
        program.ShouldContain("The restart durability data directory must be absolute.");
        program.ShouldContain("RestartDurabilityScenario.SeedAsync(restartDataDirectory)");
        program.ShouldContain("RestartDurabilityScenario.RecoverAsync(restartDataDirectory)");
        program.ShouldNotContain("System.Reflection");

        restart.ShouldContain("new(2026, 8, 7, 8, 0, 0, TimeSpan.Zero)");
        restart.ShouldContain("LeaseUntil = SeedAt.AddMinutes(1)");
        restart.ShouldContain("RecoveryAt = SeedAt.AddMinutes(2)");
        restart.ShouldContain("new DurableInputLeaseRequest(");
        restart.ShouldContain("\"restart-seed-input\"");
        restart.ShouldContain("new DurableOutputDeliveryLeaseRequest(");
        restart.ShouldContain("\"restart-seed-output\"");
        restart.ShouldContain("inputLease.OwnerId");
        restart.ShouldContain("inputLease.LeasedAt == SeedAt");
        restart.ShouldContain("inputLease.Attempt == 1");
        restart.ShouldContain("inputLease.LeaseToken != Guid.Empty");
        restart.ShouldContain("outputLease.OwnerId");
        restart.ShouldContain("outputLease.LeasedAt == SeedAt");
        restart.ShouldContain("outputLease.Attempt == 1");
        restart.ShouldContain("outputLease.LeaseToken != Guid.Empty");

        restart.ShouldContain("Host.CreateApplicationBuilder()");
        restart.ShouldContain("AddSingleton<TimeProvider>(new FixedUtcTimeProvider(RecoveryAt))");
        restart.ShouldContain("builder.Services.AddFluxFlow(Definition)");
        restart.ShouldContain(".Advanced.AddDynamicComponent(\"acceptance.restart.uppercase\"");
        restart.ShouldNotContain(".AddRuntimeComponent(");
        restart.ShouldContain("AddFluxFlowSqlFileDurableInput");
        restart.ShouldContain("AddFluxFlowSqlFileDurableOutput");
        restart.ShouldContain("AddFluxFlowDurableInput(options =>");
        restart.ShouldContain("RestartJsonContext.Default.String");
        restart.ShouldContain("outputs.Capture(");
        restart.ShouldContain("AddSingleton<IDurableOutputDeliveryHandler>(deliveryHandler)");
        restart.ShouldContain("AddFluxFlowDurableOutputDelivery(options =>");
        restart.ShouldContain("input.DeliveredCount == 1 && output.CompletedCount == 2");
        restart.ShouldContain("status.TotalCount == 1");
        restart.ShouldContain("status.CapturedCount == 2");
        restart.ShouldContain("status.UnmaterializedCount == 0");

        restart.ShouldContain("FileMode.CreateNew");
        restart.ShouldContain("envelope.Key.Address.Value");
        restart.ShouldContain("envelope.Key.MessageId.Value");
        restart.ShouldContain("A durable output identity was reused with different effect content.");
        restart.ShouldContain("records.Count == 2");
        restart.ShouldContain("PreappliedOutputValue");
        restart.ShouldContain("WorkflowOutputValue");
        restart.IndexOf("exactly-once", StringComparison.OrdinalIgnoreCase).ShouldBe(-1);
        restart.ShouldNotContain("System.Reflection");
        restart.ShouldNotContain("Thread.Sleep");
        restart.ShouldContain("new CancellationTokenSource(TimeSpan.FromSeconds(20))");
        restart.ShouldContain("WaitAsync(timeout.Token)");
        restart.ShouldContain("WaitAsync(TimeSpan.FromSeconds(5))");

        restart.ShouldContain("InputMessageId = \"restart-input\"");
        restart.ShouldContain("PreappliedOutputMessageId = \"restart-preapplied-output\"");
        restart.ShouldContain("PACKAGE_ACCEPTANCE_RESTART_SEED_INPUT={input.MessageId.Value}");
        restart.ShouldContain(
            "PACKAGE_ACCEPTANCE_RESTART_SEED_OUTPUT={preappliedOutput.MessageId.Value}");
        CountOccurrences(restart, RestartSeedMarkers[2]).ShouldBe(1);
        foreach (var marker in RestartRecoveryMarkers)
            CountOccurrences(restart, marker).ShouldBe(1);
    }

    [Fact]
    public void Acceptance_gate_is_part_of_the_complete_ci_rehearsal()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        const string acceptanceCommand = "./eng/package-consumer-acceptance.ps1 -PackPackages";
        CountOccurrences(workflow, acceptanceCommand).ShouldBe(1);
        workflow.IndexOf("- name: Test", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        workflow.IndexOf(acceptanceCommand, StringComparison.Ordinal).ShouldBeGreaterThan(
            workflow.IndexOf("- name: Test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Acceptance_script_prepare_only_resolves_exact_closure_without_mutation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory,
            "-PrepareOnly");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PACK_PACKAGES=False");
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PREPARED=True");
        ReadLines(result.StandardOutput)
            .Where(line => line.StartsWith("PACKAGE_ACCEPTANCE_CANDIDATE=", StringComparison.Ordinal))
            .ShouldBe(fixture.Packages.Select(package =>
                $"PACKAGE_ACCEPTANCE_CANDIDATE={package.Alias}|{package.PackageId}|{package.Version}"));

        var restoreCommand = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_RESTORE_COMMAND=");
        restoreCommand.ShouldContain(" --no-cache --packages ");
        restoreCommand.ShouldContain($"--packages {fixture.PackageCache}");
        restoreCommand.ShouldContain($"--configfile {Path.Combine(fixture.WorkDirectory, "NuGet.config")}");
        restoreCommand.ShouldNotContain("--source");
        AssertTopLevelVersionArguments(restoreCommand, fixture.Packages);

        var defaultCommand = ReadOutputValue(
            result.StandardOutput,
            "PACKAGE_ACCEPTANCE_RUN_COMMAND=");
        var seedCommand = ReadOutputValue(
            result.StandardOutput,
            "PACKAGE_ACCEPTANCE_RESTART_SEED_COMMAND=");
        var recoveryCommand = ReadOutputValue(
            result.StandardOutput,
            "PACKAGE_ACCEPTANCE_RESTART_RECOVERY_COMMAND=");
        var restartDataDirectory = Path.Combine(fixture.WorkDirectory, "restart-durability");
        defaultCommand.ShouldNotContain("durability-restart-");
        seedCommand.ShouldEndWith($"-- durability-restart-seed {restartDataDirectory}");
        recoveryCommand.ShouldEndWith($"-- durability-restart-recover {restartDataDirectory}");
        foreach (var command in new[] { defaultCommand, seedCommand, recoveryCommand })
            AssertTopLevelVersionArguments(command, fixture.Packages);

        Directory.Exists(fixture.PackageSource).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
        File.Exists(fixture.DotnetProcessesPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Acceptance_script_rejects_a_fixture_project_reference_before_process_execution()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);
        var rejectedFixture = Directory.CreateDirectory(Path.Combine(fixture.Root, "rejected-fixture")).FullName;
        var project = XDocument.Load(GetFixturePath(root, FixtureProject));
        project.Root!.Add(new XElement(
            "ItemGroup",
            new XElement("ProjectReference", new XAttribute("Include", "repository.csproj"))));
        project.Save(Path.Combine(rejectedFixture, FixtureProject));
        foreach (var sourcePath in Directory.GetFiles(
                     Path.Combine(root, "eng", FixtureDirectory),
                     "*.cs",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(sourcePath, Path.Combine(rejectedFixture, Path.GetFileName(sourcePath)));
        }

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-FixturePath",
            rejectedFixture,
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory,
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("cannot contain ProjectReference");
        Directory.Exists(fixture.PackageSource).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("engine-durable-output-sqlfile")]
    public async Task Acceptance_script_rejects_incomplete_candidate_closure_before_restore(
        string missingAlias)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var missing = fixture.Packages.Single(package => package.Alias == missingAlias);
        File.Delete(fixture.GetCandidateArchive(missing));

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain($"{missing.PackageId}.{missing.Version}.nupkg");
        result.ToString().ShouldContain("must contain exactly one");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
    }

    [Fact]
    public async Task Acceptance_script_restores_verifies_builds_and_runs_from_retained_isolated_workdir()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldBe(0, result.ToString());
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.Length.ShouldBe(5);
        invocations[0].ShouldStartWith("restore ");
        invocations[1].ShouldStartWith("build ");
        invocations[2].ShouldStartWith("run ");
        invocations[3].ShouldStartWith("run ");
        invocations[4].ShouldStartWith("run ");

        CountOccurrences(invocations[0], "--no-cache").ShouldBe(1);
        CountOccurrences(invocations[0], "--packages").ShouldBe(1);
        CountOccurrences(invocations[0], "--configfile").ShouldBe(1);
        invocations[0].ShouldContain($"--packages {fixture.PackageCache}");
        invocations[0].ShouldContain($"--configfile {Path.Combine(fixture.WorkDirectory, "NuGet.config")}");
        invocations[0].ShouldNotContain("--source");
        invocations[1].ShouldContain("--no-restore");
        invocations[2].ShouldContain("--no-build");
        invocations[2].ShouldContain("--no-restore");
        var restartDataDirectory = Path.Combine(fixture.WorkDirectory, "restart-durability");
        invocations[2].ShouldNotContain("durability-restart-");
        invocations[3].ShouldEndWith($"-- durability-restart-seed {restartDataDirectory}");
        invocations[4].ShouldEndWith($"-- durability-restart-recover {restartDataDirectory}");
        foreach (var invocation in invocations)
            AssertTopLevelVersionArguments(invocation, fixture.Packages);

        var processInvocations = File.ReadAllLines(fixture.DotnetProcessesPath)
            .Select(line => line.Split('|', 2))
            .ToArray();
        processInvocations.Length.ShouldBe(5);
        processInvocations.Select(parts => parts[1]).ShouldBe(invocations);
        processInvocations.Skip(2).Select(parts => parts[0]).Distinct().Count().ShouldBe(3);

        Directory.GetFiles(
                Path.Combine(root, "eng", FixtureDirectory),
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ShouldBe(Directory.GetFiles(
                    fixture.WorkDirectory,
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(fileName => fileName, StringComparer.Ordinal));

        var sourceConfig = XDocument.Load(Path.Combine(fixture.WorkDirectory, "NuGet.config"));
        var packageSources = sourceConfig
            .Descendants("packageSources")
            .Single()
            .Elements()
            .ToArray();
        packageSources.Select(element => element.Name.LocalName).ShouldBe(["clear", "add", "add"]);
        packageSources.Skip(1).Select(element => element.Attribute("key")!.Value)
            .ShouldBe(["candidate", "public"]);
        packageSources.Skip(1).Select(element => element.Attribute("value")!.Value)
            .ShouldBe([fixture.PackageSource, "https://api.nuget.org/v3/index.json"]);

        ReadLines(result.StandardOutput)
            .Where(line => line.StartsWith("PACKAGE_ACCEPTANCE_VERIFIED=", StringComparison.Ordinal))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ShouldBe(fixture.Packages
                .Select(package => $"PACKAGE_ACCEPTANCE_VERIFIED={package.PackageId}/{package.Version}")
                .OrderBy(line => line, StringComparer.Ordinal));
        foreach (var marker in AllRequiredMarkers())
            CountOccurrences(result.StandardOutput, marker).ShouldBe(1);
        CountOccurrences(result.StandardOutput, "PACKAGE_ACCEPTANCE_RESTART_COMPLETE=True")
            .ShouldBe(1);
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_COMPLETE=True");

        Directory.Exists(fixture.WorkDirectory).ShouldBeTrue();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
        foreach (var package in fixture.Packages)
        {
            var cachedArchive = Path.Combine(
                fixture.PackageCache,
                package.PackageId.ToLowerInvariant(),
                package.Version.ToLowerInvariant(),
                $"{package.PackageId.ToLowerInvariant()}.{package.Version.ToLowerInvariant()}.nupkg");
            File.ReadAllBytes(cachedArchive).ShouldBe(File.ReadAllBytes(fixture.GetCandidateArchive(package)));
        }
    }

    [Fact]
    public async Task Acceptance_script_stops_before_recovery_when_seed_marker_is_missing()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var environment = fixture.CreateEnvironment();
        environment["FAKE_DOTNET_OMIT_MARKER"] = "PACKAGE_ACCEPTANCE_RESTART_SEED_OK=True";

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            environment,
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("Package consumer restart seed must emit");
        result.ToString().ShouldContain("PACKAGE_ACCEPTANCE_RESTART_SEED_OK=True");
        result.ToString().ShouldContain("observed 0");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_RESTART_COMPLETE=True");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.Length.ShouldBe(4);
        invocations[3].ShouldContain("-- durability-restart-seed ");
        invocations.ShouldNotContain(invocation =>
            invocation.Contains("durability-restart-recover", StringComparison.Ordinal));
        Directory.Exists(fixture.WorkDirectory).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.WorkDirectory, "RestartDurabilityScenario.cs"))
            .ShouldBeTrue();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
    }

    public static TheoryData<string, string> InvalidResolutionModes =>
        new()
        {
            { "project", "resolved a project library" },
            { "hash", "does not match candidate" }
        };

    [Theory]
    [MemberData(nameof(InvalidResolutionModes))]
    public async Task Acceptance_script_rejects_project_or_non_candidate_resolution(
        string mode,
        string expectedFailureFragment)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var environment = fixture.CreateEnvironment();
        environment["FAKE_DOTNET_PROJECT_LIBRARY"] = mode == "project" ? "true" : null;
        environment["FAKE_DOTNET_MISMATCH_PACKAGE"] = mode == "hash" ? "FluxFlow.Engine" : null;

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            environment,
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain(expectedFailureFragment);
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.ShouldHaveSingleItem().ShouldStartWith("restore ");
        Directory.Exists(fixture.WorkDirectory).ShouldBeTrue();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
    }

    [Theory]
    [InlineData("omit", 0)]
    [InlineData("duplicate", 2)]
    public async Task Acceptance_script_cleans_owned_workdir_after_invalid_recovery_marker_count(
        string markerMutation,
        int observedCount)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var environment = fixture.CreateEnvironment();
        const string invalidMarker = "PACKAGE_ACCEPTANCE_RESTART_IDEMPOTENCY_OK=True";
        environment["FAKE_DOTNET_OMIT_MARKER"] = markerMutation == "omit" ? invalidMarker : null;
        environment["FAKE_DOTNET_DUPLICATE_MARKER"] =
            markerMutation == "duplicate" ? invalidMarker : null;
        var candidateBytes = fixture.Packages.ToDictionary(
            package => package.PackageId,
            package => File.ReadAllBytes(fixture.GetCandidateArchive(package)),
            StringComparer.Ordinal);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            environment,
            "-PackageSource",
            fixture.PackageSource);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("Package consumer restart recovery must emit");
        result.ToString().ShouldContain(invalidMarker);
        var expectedRecoveryFailure =
            $"Package consumer restart recovery must emit '{invalidMarker}' exactly once; observed {observedCount}.";
        CountOccurrences(NormalizeDiagnosticText(result.StandardError), expectedRecoveryFailure)
            .ShouldBe(1, $"Expected exact recovery clause: \"{expectedRecoveryFailure}\"");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_RESTART_COMPLETE=True");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        File.ReadAllLines(fixture.DotnetArgumentsPath).Length.ShouldBe(5);

        var ownedWorkDirectory = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_WORK_DIR=");
        Directory.Exists(ownedWorkDirectory).ShouldBeFalse();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
        foreach (var package in fixture.Packages)
            File.ReadAllBytes(fixture.GetCandidateArchive(package)).ShouldBe(candidateBytes[package.PackageId]);
    }

    [Fact]
    public async Task Acceptance_script_pack_mode_cleans_owned_source_and_workdir_after_success()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackPackages");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PACK_PACKAGES=True");
        foreach (var marker in AllRequiredMarkers())
            CountOccurrences(result.StandardOutput, marker).ShouldBe(1);
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_RESTART_COMPLETE=True");
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.Length.ShouldBe(15);
        invocations.Take(10).ShouldAllBe(invocation => invocation.StartsWith("pack ", StringComparison.Ordinal));
        invocations[10].ShouldStartWith("restore ");
        invocations[11].ShouldStartWith("build ");
        invocations[12].ShouldStartWith("run ");
        invocations[13].ShouldContain("-- durability-restart-seed ");
        invocations[14].ShouldContain("-- durability-restart-recover ");

        var ownedPackageSource = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_PACKAGE_SOURCE=");
        var ownedWorkDirectory = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_WORK_DIR=");
        Directory.Exists(ownedPackageSource).ShouldBeFalse();
        Directory.Exists(ownedWorkDirectory).ShouldBeFalse();
    }

    private static string GetFixturePath(string root, string fileName)
        => Path.Combine(root, "eng", FixtureDirectory, fileName);

    private static RequiredPackage[] ReadRequiredPackages(string root)
    {
        var manifest = PackageManifest.Read(root);
        return RequiredAliases.Select(alias =>
        {
            var entry = manifest.Single(package => package.Alias == alias);
            var projectPath = Path.Combine(root, NormalizePath(entry.Project));
            var project = XDocument.Load(projectPath);
            var version = project
                .Descendants()
                .Where(element => element.Name.LocalName == "Version")
                .Select(element => element.Value.Trim())
                .First(value => value.Length > 0);
            return new RequiredPackage(alias, entry.PackageId, version, projectPath);
        }).ToArray();
    }

    private static void AssertTopLevelVersionArguments(
        string invocation,
        IReadOnlyCollection<RequiredPackage> packages)
    {
        foreach (var expected in ExpectedFixturePackageVersions.Where(expected =>
                     expected.Key.StartsWith("FluxFlow.", StringComparison.Ordinal)))
        {
            var alias = expected.Key switch
            {
                "FluxFlow.Engine" => "engine",
                "FluxFlow.Engine.HealthChecks" => "engine-healthchecks",
                "FluxFlow.Fluent" => "fluent",
                "FluxFlow.Engine.DurableInput.SqlFile" => "engine-durable-input-sqlfile",
                "FluxFlow.Engine.DurableOutput.SqlFile" => "engine-durable-output-sqlfile",
                _ => throw new InvalidOperationException($"Unexpected top-level package '{expected.Key}'.")
            };
            var package = packages.Single(candidate => candidate.Alias == alias);
            var propertyName = expected.Value.TrimStart('$', '(').TrimEnd(')');
            var expectedArgument = $"-p:{propertyName}={package.Version}";
            var arguments = invocation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var exactCount = arguments.Count(argument => argument == expectedArgument);
            var commandWrapperCount = arguments
                .Zip(arguments.Skip(1))
                .Count(pair => pair.First == $"-p:{propertyName}" && pair.Second == package.Version);
            (exactCount + commandWrapperCount).ShouldBe(1, invocation);
        }
    }

    private static IEnumerable<string> AllRequiredMarkers()
        => DefaultMarkers().Concat(RestartSeedMarkers).Concat(RestartRecoveryMarkers);

    private static string[] DefaultMarkers()
        =>
        [
            "PACKAGE_ACCEPTANCE_ENGINE_OK=True",
            "PACKAGE_ACCEPTANCE_CODE_FIRST_OK=True",
            "PACKAGE_ACCEPTANCE_RESOURCE_OK=True",
            "PACKAGE_ACCEPTANCE_HEALTH_OK=True",
            "PACKAGE_ACCEPTANCE_FLUENT_OK=True",
            "PACKAGE_ACCEPTANCE_DURABILITY_OK=True",
            "PACKAGE_ACCEPTANCE_OK=True"
        ];

    private static string[] ReadLines(string text)
        => text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ReadOutputValue(string output, string prefix)
    {
        var line = ReadLines(output).Single(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static string NormalizeDiagnosticText(string value)
    {
        var withoutControlSequences = Regex.Replace(
            value,
            @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\u0007]*(?:\u0007|\x1B\\))",
            string.Empty,
            RegexOptions.CultureInvariant);
        var withoutPowerShellErrorGutters = Regex.Replace(
            withoutControlSequences,
            @"(?m)^[\t ]*\|[\t ]?",
            string.Empty,
            RegexOptions.CultureInvariant);
        return Regex.Replace(
            withoutPowerShellErrorGutters,
            @"\s+",
            " ",
            RegexOptions.CultureInvariant).Trim();
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string ExtractSourceSection(
        string source,
        string startMarker,
        string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return source[start..end];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private sealed record RequiredPackage(
        string Alias,
        string PackageId,
        string Version,
        string ProjectPath);

    private sealed class AcceptanceTestFixture : IDisposable
    {
        private AcceptanceTestFixture(string repositoryRoot, bool createCandidateSource)
        {
            Root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(),
                $"fluxflow-package-acceptance-test-{Guid.NewGuid():N}")).FullName;
            Packages = ReadRequiredPackages(repositoryRoot);
            PackageSource = Path.Combine(Root, "candidate-source");
            WorkDirectory = Path.Combine(Root, "consumer-work");
            PackageCache = Path.Combine(WorkDirectory, "packages");
            DotnetArgumentsPath = Path.Combine(Root, "dotnet-arguments.txt");
            DotnetProcessesPath = Path.Combine(Root, "dotnet-processes.txt");
            DotnetCurrentArgumentsPath = Path.Combine(Root, "dotnet-current-arguments.txt");
            var metadataPath = Path.Combine(Root, "packages.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(Packages));
            var driverPath = Path.Combine(Root, "fake-dotnet.ps1");
            File.WriteAllText(driverPath, FakeDotnetDriver);
            FakeDotnetDirectory = CreateFakeDotnetCommand(Root);
            Environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FAKE_DOTNET_ARGUMENTS"] = DotnetArgumentsPath,
                ["FAKE_DOTNET_PROCESSES"] = DotnetProcessesPath,
                ["FAKE_DOTNET_CURRENT_ARGUMENTS"] = DotnetCurrentArgumentsPath,
                ["FAKE_DOTNET_DRIVER"] = driverPath,
                ["FAKE_DOTNET_METADATA"] = metadataPath,
                ["PATH"] = FakeDotnetDirectory + Path.PathSeparator +
                    System.Environment.GetEnvironmentVariable("PATH")
            };

            if (createCandidateSource)
            {
                Directory.CreateDirectory(PackageSource);
                foreach (var package in Packages)
                {
                    File.WriteAllText(
                        GetCandidateArchive(package),
                        $"candidate:{package.PackageId}/{package.Version}");
                }
            }
        }

        public string Root { get; }

        public RequiredPackage[] Packages { get; }

        public string PackageSource { get; }

        public string WorkDirectory { get; }

        public string PackageCache { get; }

        public string DotnetArgumentsPath { get; }

        public string DotnetProcessesPath { get; }

        private string DotnetCurrentArgumentsPath { get; }

        private string FakeDotnetDirectory { get; }

        private Dictionary<string, string?> Environment { get; }

        public static AcceptanceTestFixture Create(
            string repositoryRoot,
            bool createCandidateSource = true)
            => new(repositoryRoot, createCandidateSource);

        public Dictionary<string, string?> CreateEnvironment()
            => new(Environment, StringComparer.OrdinalIgnoreCase);

        public string GetCandidateArchive(RequiredPackage package)
            => Path.Combine(PackageSource, $"{package.PackageId}.{package.Version}.nupkg");

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string CreateFakeDotnetCommand(string root)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "fake-bin")).FullName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.WriteAllText(Path.Combine(directory, "dotnet.cmd"), """
                    @echo off
                    type nul > "%FAKE_DOTNET_CURRENT_ARGUMENTS%"
                    :collect_arguments
                    if "%~1"=="" goto run_driver
                    >> "%FAKE_DOTNET_CURRENT_ARGUMENTS%" echo(%~1
                    shift
                    goto collect_arguments
                    :run_driver
                    pwsh -NoLogo -NoProfile -File "%FAKE_DOTNET_DRIVER%"
                    exit /b %ERRORLEVEL%
                    """);
                return directory;
            }

            var scriptPath = Path.Combine(directory, "dotnet");
            File.WriteAllText(scriptPath, """
                #!/usr/bin/env bash
                printf '%s\n' "$@" > "$FAKE_DOTNET_CURRENT_ARGUMENTS"
                exec pwsh -NoLogo -NoProfile -File "$FAKE_DOTNET_DRIVER"
                """.ReplaceLineEndings("\n"));
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return directory;
        }

        private const string FakeDotnetDriver = """
            $ErrorActionPreference = "Stop"
            $CommandArguments = @(Get-Content -LiteralPath $env:FAKE_DOTNET_CURRENT_ARGUMENTS)
            Add-Content -LiteralPath $env:FAKE_DOTNET_ARGUMENTS -Value ($CommandArguments -join " ")
            Add-Content -LiteralPath $env:FAKE_DOTNET_PROCESSES -Value "$PID|$($CommandArguments -join " ")"
            $packages = @(Get-Content -LiteralPath $env:FAKE_DOTNET_METADATA -Raw | ConvertFrom-Json)

            function Read-Argument {
                param([string] $Name)

                for ($index = 0; $index -lt $CommandArguments.Count - 1; $index++) {
                    if ($CommandArguments[$index] -eq $Name) {
                        return $CommandArguments[$index + 1]
                    }
                }

                throw "Fake dotnet did not receive '$Name'."
            }

            $operation = $CommandArguments[0]
            if ($operation -eq "pack") {
                $projectPath = [System.IO.Path]::GetFullPath($CommandArguments[1])
                $package = @($packages | Where-Object {
                    [string]::Equals(
                        [System.IO.Path]::GetFullPath($_.ProjectPath),
                        $projectPath,
                        [System.StringComparison]::OrdinalIgnoreCase)
                })[0]
                if ($null -eq $package) {
                    throw "Fake dotnet could not map pack project '$projectPath'."
                }

                $outputPath = Read-Argument "--output"
                New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
                Set-Content `
                    -LiteralPath (Join-Path $outputPath "$($package.PackageId).$($package.Version).nupkg") `
                    -Value "candidate:$($package.PackageId)/$($package.Version)" `
                    -NoNewline
                exit 0
            }

            if ($operation -eq "restore") {
                $projectPath = [System.IO.Path]::GetFullPath($CommandArguments[1])
                $workRoot = Split-Path -Parent $projectPath
                $packageCache = Read-Argument "--packages"
                $configPath = Read-Argument "--configfile"
                [xml] $config = Get-Content -LiteralPath $configPath -Raw
                $candidateSource = [string] (@($config.configuration.packageSources.add | Where-Object {
                    $_.key -eq "candidate"
                })[0].value)

                $libraries = [ordered]@{}
                if ($env:FAKE_DOTNET_PROJECT_LIBRARY -eq "true") {
                    $libraries["Repository.Project/1.0.0"] = [ordered]@{
                        type = "project"
                        path = "../src/Repository.Project.csproj"
                    }
                }

                foreach ($package in $packages) {
                    $lowerId = $package.PackageId.ToLowerInvariant()
                    $lowerVersion = $package.Version.ToLowerInvariant()
                    $relativePath = "$lowerId/$lowerVersion"
                    $libraries["$($package.PackageId)/$($package.Version)"] = [ordered]@{
                        type = "package"
                        path = $relativePath
                    }

                    $packageDirectory = Join-Path $packageCache $relativePath
                    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
                    $candidateArchive = Join-Path $candidateSource "$($package.PackageId).$($package.Version).nupkg"
                    $cachedArchive = Join-Path $packageDirectory "$lowerId.$lowerVersion.nupkg"
                    Copy-Item -LiteralPath $candidateArchive -Destination $cachedArchive
                    if ($env:FAKE_DOTNET_MISMATCH_PACKAGE -eq $package.PackageId) {
                        Add-Content -LiteralPath $cachedArchive -Value "mismatch"
                    }
                }

                $assetsDirectory = Join-Path $workRoot "obj"
                New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
                [ordered]@{
                    version = 3
                    libraries = $libraries
                } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $assetsDirectory "project.assets.json")
                exit 0
            }

            if ($operation -eq "run") {
                $mode = @($CommandArguments | Where-Object { $_ -like "durability-restart-*" })
                $markers = if ($mode.Count -eq 0) {
                    @(
                        "PACKAGE_ACCEPTANCE_ENGINE_OK=True",
                        "PACKAGE_ACCEPTANCE_CODE_FIRST_OK=True",
                        "PACKAGE_ACCEPTANCE_RESOURCE_OK=True",
                        "PACKAGE_ACCEPTANCE_HEALTH_OK=True",
                        "PACKAGE_ACCEPTANCE_FLUENT_OK=True",
                        "PACKAGE_ACCEPTANCE_DURABILITY_OK=True",
                        "PACKAGE_ACCEPTANCE_OK=True"
                    )
                }
                elseif ($mode[0] -eq "durability-restart-seed") {
                    @(
                        "PACKAGE_ACCEPTANCE_RESTART_SEED_INPUT=restart-input",
                        "PACKAGE_ACCEPTANCE_RESTART_SEED_OUTPUT=restart-preapplied-output",
                        "PACKAGE_ACCEPTANCE_RESTART_SEED_OK=True"
                    )
                }
                elseif ($mode[0] -eq "durability-restart-recover") {
                    @(
                        "PACKAGE_ACCEPTANCE_RESTART_INPUT_RECOVERED=True",
                        "PACKAGE_ACCEPTANCE_RESTART_WORKFLOW_OUTPUT_CAPTURED=True",
                        "PACKAGE_ACCEPTANCE_RESTART_PENDING_OUTPUT_RESUMED=True",
                        "PACKAGE_ACCEPTANCE_RESTART_OUTPUT_RECOVERED=True",
                        "PACKAGE_ACCEPTANCE_RESTART_IDEMPOTENCY_OK=True",
                        "PACKAGE_ACCEPTANCE_RESTART_OK=True"
                    )
                }
                else {
                    throw "Fake dotnet received unexpected acceptance mode '$($mode[0])'."
                }
                foreach ($marker in $markers) {
                    if ($marker -ne $env:FAKE_DOTNET_OMIT_MARKER) {
                        Write-Output $marker
                        if ($marker -eq $env:FAKE_DOTNET_DUPLICATE_MARKER) {
                            Write-Output $marker
                        }
                    }
                }
            }

            exit 0
            """;
    }
}
