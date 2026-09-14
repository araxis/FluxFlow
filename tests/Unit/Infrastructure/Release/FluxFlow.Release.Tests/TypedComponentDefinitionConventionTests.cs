using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class TypedComponentDefinitionConventionTests
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

    private static readonly string[] FamilyDefinitionSources =
    [
        "src/Components/Assertions/FluxFlow.Components.Assertions.Composition/AssertionsDefinitionExtensions.cs",
        "src/Components/Expectations/FluxFlow.Components.Expectations.Composition/ExpectationsDefinitionExtensions.cs",
        "src/Components/FileSystem/FluxFlow.Components.FileSystem.Composition/FileSystemDefinitionExtensions.cs",
        "src/Components/Http/FluxFlow.Components.Http.Composition/HttpDefinitionExtensions.cs",
        "src/Components/Mapping/FluxFlow.Components.Mapping.Composition/MappingDefinitionExtensions.cs",
        "src/Components/Metrics/FluxFlow.Components.Metrics.Composition/MetricsDefinitionExtensions.cs",
        "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/Definition/MqttApplicationDefinitionExtensions.cs",
        "src/Components/Observability/FluxFlow.Components.Observability.Composition/ObservabilityDefinitionExtensions.cs",
        "src/Components/Payloads/FluxFlow.Components.Payloads.Composition/PayloadsDefinitionExtensions.cs",
        "src/Components/Projections/FluxFlow.Components.Projections.Composition/ProjectionsDefinitionExtensions.cs",
        "src/Components/Resilience/FluxFlow.Components.Resilience.Composition/ResilienceDefinitionExtensions.cs",
        "src/Components/Routing/FluxFlow.Components.Routing.Composition/RoutingDefinitionExtensions.cs",
        "src/Components/Serialization/FluxFlow.Components.Serialization.Composition/SerializationDefinitionExtensions.cs",
        "src/Components/Sessions/FluxFlow.Components.Sessions.Composition/SessionsDefinitionExtensions.cs",
        "src/Components/Sources/FluxFlow.Components.Sources.Composition/SourcesDefinitionExtensions.cs",
        "src/Components/State/FluxFlow.Components.State.Composition/StateDefinitionExtensions.cs",
        "src/Components/Storage/FluxFlow.Components.Storage.Composition/StorageDefinitionExtensions.cs",
        "src/Components/Timers/FluxFlow.Components.Timers.Composition/TimersDefinitionExtensions.cs",
        "src/Components/Validation/FluxFlow.Components.Validation.Composition/ValidationDefinitionExtensions.cs"
    ];

    private static readonly DefinitionScope[] SampleAndAcceptanceScopes =
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
    public void Official_family_sources_share_direct_factories_without_component_contracts()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        FamilyRegistrationSources.Length.ShouldBe(19);
        FamilyDefinitionSources.Length.ShouldBe(19);
        var contractDeclarations = 0;
        var contractRegistrations = 0;
        for (var index = 0; index < FamilyRegistrationSources.Length; index++)
        {
            var registrationPath = FamilyRegistrationSources[index];
            var definitionPath = FamilyDefinitionSources[index];
            var registration = ReadSource(root, registrationPath);
            var definition = ReadSource(root, definitionPath);
            var combined = registration + Environment.NewLine + definition;

            const string registrationMethod = ".AddComponent(";
            registration.ShouldContain(registrationMethod, Case.Sensitive);
            combined.ShouldNotContain("ComponentContract", Case.Sensitive);
            combined.ShouldNotContain("ComponentNodeActivation", Case.Sensitive);
            combined.ShouldNotContain(".AddDynamicComponent(", Case.Sensitive);
            definition.ShouldContain("workflow.AddComponent(name, ", Case.Sensitive);
            // MQTT keeps its node callbacks in a dedicated file beside its resource registration.
            var factories = registrationPath.Contains("/Mqtt/", StringComparison.Ordinal)
                ? ReadSource(root, "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/MqttCompositionNodeFactories.cs")
                : registration;
            factories.ShouldContain("context.HasOutput(", Case.Sensitive);
            factories.ShouldContain("context.HasEvents(", Case.Sensitive);
            AssertNoDuplicatedRuntimePortConstruction(registrationPath, combined);
            contractDeclarations += CountOccurrences(definition, "public static ComponentContract<");
            contractRegistrations += CountOccurrences(registration, registrationMethod);
        }

        contractDeclarations.ShouldBe(0);
        contractRegistrations.ShouldBe(45);
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
                "workflow.AddComponent(",
                Case.Sensitive,
                $"{scope.Name} must construct complete code-first contracts.");
            source.ShouldContain(
                "ComponentActivationContext context",
                Case.Sensitive,
                $"{scope.Name} must show direct typed node factories.");
            source.ShouldContain(
                "context.HasEvents(",
                Case.Sensitive,
                $"{scope.Name} must show explicit event-port binding.");
            AssertNoDuplicatedRuntimePortConstruction(scope.Name, source);
            source.ShouldNotContain("ComponentAuthoringContract", Case.Sensitive);
            source.ShouldNotContain(".AddRuntimeComponent(", Case.Sensitive,
                $"{scope.Name} must rely on embedded contracts for its normal code-first path.");
            source.ShouldNotContain(".AddDynamicComponent(", Case.Sensitive,
                $"{scope.Name} must not use the advanced dynamic escape hatch.");
            source.ShouldNotContain(".UseInstanceFactory(", Case.Sensitive,
                $"{scope.Name} must demonstrate normal typed definition, not the advanced escape hatch.");
        }
    }

    [Fact]
    public void Mqtt_declares_four_executable_resource_contracts_and_only_configuration_path_registers_AddMqtt()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var definition = ReadSource(
            root,
            "src/Components/Mqtt/FluxFlow.Components.Mqtt.Composition/Definition/MqttApplicationDefinitionExtensions.cs");
        var sample = ReadSource(
            root,
            "samples/FluxFlow.MqttCompositionSample/Program.cs");

        definition.ShouldContain("public static class MqttResources", Case.Sensitive);
        CountOccurrences(definition, "public static ApplicationResourceContract<")
            .ShouldBe(4);
        CountOccurrences(definition, "ApplicationResourceContract.Create(")
            .ShouldBe(4);
        definition.ShouldContain("MqttCompositionResourceRegistrar", Case.Sensitive);

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
                "src/Core/Application/FluxFlow.Composition/Definition/ApplicationResourceContract.cs",
                "src/Core/Application/FluxFlow.Composition/Definition/ApplicationDefinitionBuilder.cs",
                "src/Core/Application/FluxFlow.Composition/Resources/ApplicationResourceRegistration.cs"
            }.Select(path => ReadSource(root, path)));

        source.ShouldContain("ResourceContracts", Case.Sensitive);
        source.ShouldContain("RegistrationIdentity", Case.Sensitive);
        source.ShouldContain("ReferenceEqualityComparer.Instance", Case.Sensitive);
        source.ShouldNotContain("System.Reflection", Case.Sensitive);
        source.ShouldNotContain("Activator.CreateInstance", Case.Sensitive);
        source.ShouldNotContain("AppDomain.CurrentDomain", Case.Sensitive);
        source.ShouldNotContain("GetAssemblies(", Case.Sensitive);
        source.ShouldNotContain("Assembly.Load(", Case.Sensitive);
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
        File.Exists(fullPath).ShouldBeTrue($"Expected definition source is missing: {relativePath}.");
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

    private sealed record DefinitionScope(string Name, IReadOnlyList<string> RelativePaths);
}
