using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class TypedComponentAuthoringConventionTests
{
    private static readonly string[] FamilyRegistrationSources =
    [
        "src/Components/Assertions/FluxFlow.Components.Assertions.Composition/AssertionsServiceCollectionExtensions.cs",
        "src/Components/Expectations/FluxFlow.Components.Expectations.Composition/ExpectationsServiceCollectionExtensions.cs",
        "src/Components/FileSystem/FluxFlow.Components.FileSystem.Composition/FileSystemServiceCollectionExtensions.cs",
        "src/Components/Http/FluxFlow.Components.Http.Composition/HttpServiceCollectionExtensions.cs",
        "src/Components/Mapping/FluxFlow.Components.Mapping.Composition/MappingServiceCollectionExtensions.cs",
        "src/Components/Metrics/FluxFlow.Components.Metrics.Composition/MetricsServiceCollectionExtensions.cs",
        "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/MqttServiceCollectionExtensions.cs",
        "src/Components/Observability/FluxFlow.Components.Observability.Composition/ObservabilityServiceCollectionExtensions.cs",
        "src/Components/Payloads/FluxFlow.Components.Payloads.Composition/PayloadsServiceCollectionExtensions.cs",
        "src/Components/Projections/FluxFlow.Components.Projections.Composition/ProjectionsServiceCollectionExtensions.cs",
        "src/Components/Resilience/FluxFlow.Components.Resilience.Composition/ResilienceServiceCollectionExtensions.cs",
        "src/Components/Routing/FluxFlow.Components.Routing.Composition/RoutingServiceCollectionExtensions.cs",
        "src/Components/Serialization/FluxFlow.Components.Serialization.Composition/SerializationServiceCollectionExtensions.cs",
        "src/Components/Sessions/FluxFlow.Components.Sessions.Composition/SessionsServiceCollectionExtensions.cs",
        "src/Components/Sources/FluxFlow.Components.Sources.Composition/SourcesServiceCollectionExtensions.cs",
        "src/Components/State/FluxFlow.Components.State.Composition/StateServiceCollectionExtensions.cs",
        "src/Components/Storage/FluxFlow.Components.Storage.Composition/StorageServiceCollectionExtensions.cs",
        "src/Components/Timers/FluxFlow.Components.Timers.Composition/TimersServiceCollectionExtensions.cs",
        "src/Components/Validation/FluxFlow.Components.Validation.Composition/ValidationServiceCollectionExtensions.cs"
    ];

    private static readonly string[] FamilyAuthoringSources =
    [
        "src/Components/Assertions/FluxFlow.Components.Assertions.Composition/AssertionsAuthoringExtensions.cs",
        "src/Components/Expectations/FluxFlow.Components.Expectations.Composition/ExpectationsAuthoringExtensions.cs",
        "src/Components/FileSystem/FluxFlow.Components.FileSystem.Composition/FileSystemAuthoringExtensions.cs",
        "src/Components/Http/FluxFlow.Components.Http.Composition/HttpAuthoringExtensions.cs",
        "src/Components/Mapping/FluxFlow.Components.Mapping.Composition/MappingAuthoringExtensions.cs",
        "src/Components/Metrics/FluxFlow.Components.Metrics.Composition/MetricsAuthoringExtensions.cs",
        "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/Authoring/MqttApplicationAuthoringExtensions.cs",
        "src/Components/Observability/FluxFlow.Components.Observability.Composition/ObservabilityAuthoringExtensions.cs",
        "src/Components/Payloads/FluxFlow.Components.Payloads.Composition/PayloadsAuthoringExtensions.cs",
        "src/Components/Projections/FluxFlow.Components.Projections.Composition/ProjectionsAuthoringExtensions.cs",
        "src/Components/Resilience/FluxFlow.Components.Resilience.Composition/ResilienceAuthoringExtensions.cs",
        "src/Components/Routing/FluxFlow.Components.Routing.Composition/RoutingAuthoringExtensions.cs",
        "src/Components/Serialization/FluxFlow.Components.Serialization.Composition/SerializationAuthoringExtensions.cs",
        "src/Components/Sessions/FluxFlow.Components.Sessions.Composition/SessionsAuthoringExtensions.cs",
        "src/Components/Sources/FluxFlow.Components.Sources.Composition/SourcesAuthoringExtensions.cs",
        "src/Components/State/FluxFlow.Components.State.Composition/StateAuthoringExtensions.cs",
        "src/Components/Storage/FluxFlow.Components.Storage.Composition/StorageAuthoringExtensions.cs",
        "src/Components/Timers/FluxFlow.Components.Timers.Composition/TimersAuthoringExtensions.cs",
        "src/Components/Validation/FluxFlow.Components.Validation.Composition/ValidationAuthoringExtensions.cs"
    ];

    private static readonly AuthoringScope[] SampleAndAcceptanceScopes =
    [
        new("Composition sample", ["samples/FluxFlow.CompositionSample/Program.cs"]),
        new(
            "Sample application",
            [
                "samples/FluxFlow.SampleApp/SampleComponentRegistration.cs",
                "samples/FluxFlow.SampleApp/OrderNodes.cs",
                "samples/FluxFlow.SampleApp/SampleWorkspaceDefinition.cs"
            ]),
        new("MQTT composition sample", ["samples/FluxFlow.MqttCompositionSample/Program.cs"]),
        new(
            "Package consumer acceptance fixture",
            ["eng/package-consumer-acceptance/Program.cs"])
    ];

    [Fact]
    public void Official_family_sources_create_one_complete_contract_and_register_it_without_duplicate_runtime_configuration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        FamilyRegistrationSources.Length.ShouldBe(19);
        FamilyAuthoringSources.Length.ShouldBe(19);
        var contractDeclarations = 0;
        var contractRegistrations = 0;
        for (var index = 0; index < FamilyRegistrationSources.Length; index++)
        {
            var registrationPath = FamilyRegistrationSources[index];
            var authoringPath = FamilyAuthoringSources[index];
            var registration = ReadSource(root, registrationPath);
            var authoring = ReadSource(root, authoringPath);
            var combined = registration + Environment.NewLine + authoring;

            registration.ShouldContain(
                ".AddDesignedComponent(",
                Case.Sensitive,
                $"{registrationPath} must register the exact designed contract.");
            registration.ShouldNotContain(
                ".AddRuntimeComponent(",
                Case.Sensitive,
                $"{registrationPath} must not duplicate contract-owned runtime configuration.");
            combined.ShouldNotContain(
                ".AddDynamicComponent(",
                Case.Sensitive,
                $"{registrationPath} is normal family authoring and must not use the advanced dynamic surface.");
            authoring.ShouldContain(
                "ComponentContract<",
                Case.Sensitive,
                $"{authoringPath} must expose typed complete contracts.");
            authoring.ShouldContain(
                "DesignedComponentContract.Create(",
                Case.Sensitive,
                $"{authoringPath} must create runtime and design metadata from one declaration.");
            combined.ShouldContain(
                ".UseFactory(",
                Case.Sensitive,
                $"{registrationPath} must use the typed node-factory path.");
            combined.ShouldContain(
                ".HasEvents(",
                Case.Sensitive,
                $"{registrationPath} must explicitly declare component events.");
            combined.ShouldNotContain("ComponentAuthoringContract", Case.Sensitive);
            AssertNoDuplicatedRuntimePortConstruction(registrationPath, combined);
            combined.ShouldNotContain(".UseInstanceFactory(", Case.Sensitive,
                $"{registrationPath} is normal family authoring and must not use the raw-instance escape hatch.");
            contractDeclarations += CountOccurrences(authoring, "public static ComponentContract<");
            contractRegistrations += CountOccurrences(registration, ".AddDesignedComponent(");
        }

        contractDeclarations.ShouldBe(44);
        contractRegistrations.ShouldBe(44);
    }

    [Fact]
    public void Normal_samples_and_package_code_first_path_use_embedded_contracts_without_redundant_runtime_registration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        SampleAndAcceptanceScopes.Length.ShouldBe(4);
        foreach (var scope in SampleAndAcceptanceScopes)
        {
            var source = string.Join(
                Environment.NewLine,
                scope.RelativePaths.Select(path => ReadSource(root, path)));

            source.ShouldContain(
                "ComponentContract.Create(",
                Case.Sensitive,
                $"{scope.Name} must construct complete code-first contracts.");
            source.ShouldContain(
                ".UseFactory(",
                Case.Sensitive,
                $"{scope.Name} must show typed node factories.");
            source.ShouldContain(
                ".HasEvents(",
                Case.Sensitive,
                $"{scope.Name} must show explicit event-port binding.");
            AssertNoDuplicatedRuntimePortConstruction(scope.Name, source);
            source.ShouldNotContain("ComponentAuthoringContract", Case.Sensitive);
            source.ShouldNotContain(".AddRuntimeComponent(", Case.Sensitive,
                $"{scope.Name} must rely on embedded contracts for its normal code-first path.");
            source.ShouldNotContain(".AddDynamicComponent(", Case.Sensitive,
                $"{scope.Name} must not use the advanced dynamic escape hatch.");
            source.ShouldNotContain(".UseInstanceFactory(", Case.Sensitive,
                $"{scope.Name} must demonstrate normal typed authoring, not the advanced escape hatch.");
        }
    }

    [Fact]
    public void Mqtt_declares_four_executable_resource_contracts_and_only_configuration_path_registers_AddMqtt()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var authoring = ReadSource(
            root,
            "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/Authoring/MqttApplicationAuthoringExtensions.cs");
        var sample = ReadSource(
            root,
            "samples/FluxFlow.MqttCompositionSample/Program.cs");

        authoring.ShouldContain("public static class MqttResources", Case.Sensitive);
        CountOccurrences(authoring, "public static ApplicationResourceContract<")
            .ShouldBe(4);
        CountOccurrences(authoring, "ApplicationResourceContract.Create(")
            .ShouldBe(4);
        authoring.ShouldContain("MqttCompositionResourceRegistrar", Case.Sensitive);

        var configurationPath = ReadSection(
            sample,
            "static async Task<IReadOnlyList<MqttPublishMessage>> RunConfigurationCompositionAsync(",
            "static async Task<IReadOnlyList<MqttPublishMessage>> RunDefinitionApplicationAsync(");
        var codeFirstPath = ReadSection(
            sample,
            "static async Task<IReadOnlyList<MqttPublishMessage>> RunDefinitionApplicationAsync(",
            "static async Task<IReadOnlyList<MqttPublishMessage>> RunHostedApplicationAsync(");

        configurationPath.ShouldContain("registerMqtt: true", Case.Sensitive);
        codeFirstPath.ShouldContain("new ApplicationDefinitionBuilder()", Case.Sensitive);
        codeFirstPath.ShouldContain(".AddMqttBroker(", Case.Sensitive);
        codeFirstPath.ShouldContain(".AddMqttClient(", Case.Sensitive);
        codeFirstPath.ShouldContain("registerMqtt: false", Case.Sensitive);
        codeFirstPath.ShouldNotContain(".AddMqtt()", Case.Sensitive);
        codeFirstPath.ShouldNotContain("ApplicationDefinitionJson", Case.Sensitive);
    }

    [Fact]
    public void Code_first_resource_execution_uses_definition_owned_contracts_without_reflection_or_global_scanning()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                "src/Core/Application/FluxFlow.Composition/Authoring/ApplicationResourceContract.cs",
                "src/Core/Application/FluxFlow.Composition/Authoring/ApplicationDefinitionBuilder.cs",
                "src/Engine/Runtime/FluxFlow.Engine/Hosting/ApplicationRuntimeResourceSnapshotFactory.cs"
            }.Select(path => ReadSource(root, path)));

        source.ShouldContain("ApplicationResourceContracts", Case.Sensitive);
        source.ShouldContain("RegistrationIdentity", Case.Sensitive);
        source.ShouldContain("ReferenceEqualityComparer.Instance", Case.Sensitive);
        source.ShouldNotContain("System.Reflection", Case.Sensitive);
        source.ShouldNotContain("Activator.CreateInstance", Case.Sensitive);
        source.ShouldNotContain("AppDomain.CurrentDomain", Case.Sensitive);
        source.ShouldNotContain("GetAssemblies(", Case.Sensitive);
        source.ShouldNotContain("Assembly.Load(", Case.Sensitive);
    }

    [Fact]
    public void Fluent_sources_compile_to_the_canonical_definition_and_application_without_a_parallel_runtime()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                "src/Authoring/FluxFlow.Fluent/FlowGraph.cs",
                "src/Authoring/FluxFlow.Fluent/FlowGraphBuilder.cs"
            }.Select(path => ReadSource(root, path)));

        source.ShouldContain("ApplicationDefinitionBuilder _application = new()", Case.Sensitive);
        source.ShouldContain("ComponentContract.Create", Case.Sensitive);
        source.ShouldContain("services.AddFluxFlow(", Case.Sensitive);
        source.ShouldContain("GetRequiredService<FluxFlowApplication>()", Case.Sensitive);
        source.ShouldContain("public ApplicationDefinition Definition", Case.Sensitive);
        source.ShouldContain("public FluxFlowApplication Application", Case.Sensitive);
        source.ShouldNotContain("ApplicationRuntime", Case.Sensitive);
        source.ShouldNotContain("FlowGraphEngine", Case.Sensitive);
        source.ShouldNotContain("ComponentInstance.Create(", Case.Sensitive);
    }

    private static void AssertNoDuplicatedRuntimePortConstruction(string owner, string source)
    {
        source.ShouldNotContain("ComponentInstance.Create(", Case.Sensitive,
            $"{owner} must let typed UseFactory selectors construct the runtime instance.");
        Regex.IsMatch(source, @"(?<![A-Za-z0-9_])ComponentPorts\.", RegexOptions.CultureInvariant)
            .ShouldBeFalse(
                $"{owner} must not repeat selected runtime blocks through low-level ComponentPorts calls.");
        foreach (var legacyPortMethod in LegacyPortMethodTokens)
        {
            source.ShouldNotContain(
                legacyPortMethod,
                Case.Sensitive,
                $"{owner} must use the canonical Has... component-port DSL without Add... aliases.");
        }
        source.ShouldNotContain("System.Reflection", Case.Sensitive,
            $"{owner} must keep typed port binding reflection-free.");
        source.ShouldNotContain("Activator.CreateInstance", Case.Sensitive,
            $"{owner} must keep typed port binding free of runtime discovery.");
    }

    private static string ReadSource(string root, string relativePath)
    {
        var fullPath = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).ShouldBeTrue($"Expected authoring source is missing: {relativePath}.");
        return File.ReadAllText(fullPath);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string ReadSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return source[start..end];
    }

    private static readonly string[] LegacyPortMethodTokens =
    [
        ".AddInput(",
        ".AddInput<",
        ".AddSignalInput(",
        ".AddOutput(",
        ".AddOutput<",
        ".AddEvents("
    ];

    private sealed record AuthoringScope(string Name, IReadOnlyList<string> RelativePaths);
}
