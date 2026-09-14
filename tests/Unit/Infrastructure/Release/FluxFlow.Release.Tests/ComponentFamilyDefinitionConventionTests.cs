using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Expectations.Composition;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Observability.Composition;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Resilience.Composition;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Serialization.Composition;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ComponentFamilyDefinitionConventionTests
{
    private static readonly FamilyCase[] DefinitionFamilies =
    [
        new(typeof(AssertionsDefinitionExtensions), ["AddAssertion"]),
        new(typeof(ExpectationsDefinitionExtensions), ["AddEventExpectation"]),
        new(
            typeof(FileSystemDefinitionExtensions),
            ["AddDirectoryEnumerate", "AddFileRead", "AddFileWatch", "AddFileWrite"]),
        new(typeof(HttpDefinitionExtensions), ["AddHttpRequest"]),
        new(typeof(MappingDefinitionExtensions), ["AddMapper"]),
        new(typeof(MetricsDefinitionExtensions), ["AddMetricAggregation"]),
        new(
            typeof(MqttApplicationDefinitionExtensions),
            ["AddMqttCommand", "AddMqttEvents", "AddMqttPublish", "AddMqttReceive"]),
        new(
            typeof(ObservabilityDefinitionExtensions),
            ["AddCounter", "AddLogger", "AddMetrics"]),
        new(typeof(PayloadsDefinitionExtensions), ["AddPayloadInspection"]),
        new(typeof(ProjectionsDefinitionExtensions), ["AddEventProjection"]),
        new(typeof(ResilienceDefinitionExtensions), ["AddFlowRetry"]),
        new(
            typeof(RoutingDefinitionExtensions),
            ["AddCorrelation", "AddJoin", "AddWindow"]),
        new(
            typeof(SerializationDefinitionExtensions),
            [
                "AddBase64Decode",
                "AddBase64Encode",
                "AddJsonParse",
                "AddJsonStringify",
                "AddTextDecode",
                "AddTextEncode"
            ]),
        new(
            typeof(SessionsDefinitionExtensions),
            ["AddSessionQuery", "AddSessionRecorder", "AddSessionReplay"]),
        new(
            typeof(SourcesDefinitionExtensions),
            ["AddGeneratedSource", "AddSequenceSource"]),
        new(typeof(StateDefinitionExtensions), ["AddStateReducer"]),
        new(
            typeof(StorageDefinitionExtensions),
            ["AddStorageDelete", "AddStorageGet", "AddStoragePut", "AddStorageQuery"]),
        new(
            typeof(TimersDefinitionExtensions),
            ["AddDebounce", "AddDelay", "AddIntervalTimer", "AddScheduleTimer", "AddThrottle"]),
        new(typeof(ValidationDefinitionExtensions), ["AddJsonSchemaValidator"])
    ];

    [Fact]
    public void Explicit_19_family_matrix_exposes_complete_fluent_capture_convention()
    {
        DefinitionFamilies.Length.ShouldBe(19);
        DefinitionFamilies.Select(static family => family.DefinitionType.Assembly)
            .Distinct()
            .Count()
            .ShouldBe(19);

        var originalCount = 0;
        var optionalConfigureCount = 0;
        var fluentCount = 0;

        foreach (var family in DefinitionFamilies)
        {
            var methods = family.DefinitionType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => method.IsDefined(typeof(ExtensionAttribute), inherit: false))
                .ToArray();
            var originals = methods
                .Where(static method =>
                    method.ReturnType != typeof(WorkflowDefinitionBuilder) &&
                    IsWorkflowExtension(method) &&
                    !HasOutParameter(method))
                .ToArray();
            var fluent = methods
                .Where(static method =>
                    method.ReturnType == typeof(WorkflowDefinitionBuilder) &&
                    IsWorkflowExtension(method) &&
                    HasOutParameter(method))
                .ToArray();

            originals.Select(static method => method.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ShouldBe(family.MethodNames, ignoreOrder: false);

            foreach (var original in originals)
            {
                var configuredCapture = fluent
                    .Where(candidate => MatchesCapture(candidate, original, omitConfigure: false))
                    .ShouldHaveSingleItem(
                        $"{family.DefinitionType.Name}.{original.Name} must have one exact configured capture overload.");
                AssertCaptureShape(configuredCapture, original.ReturnType);

                if (!HasOptionalConfigure(original))
                    continue;

                var noConfigurationCapture = fluent
                    .Where(candidate => MatchesCapture(candidate, original, omitConfigure: true))
                    .ShouldHaveSingleItem(
                        $"{family.DefinitionType.Name}.{original.Name} must have one exact no-configuration capture overload.");
                AssertCaptureShape(noConfigurationCapture, original.ReturnType);
                optionalConfigureCount++;
            }

            fluent.Length.ShouldBe(
                originals.Length + originals.Count(HasOptionalConfigure),
                $"{family.DefinitionType.Name} has an unmatched or missing fluent capture overload.");
            originalCount += originals.Length;
            fluentCount += fluent.Length;
        }

        originalCount.ShouldBe(44);
        optionalConfigureCount.ShouldBe(11);
        fluentCount.ShouldBe(55);
    }

    [Fact]
    public void All_19_families_register_45_direct_factories_without_component_contracts()
    {
        var services = new ServiceCollection();
        foreach (var family in DefinitionFamilies)
        {
            family.DefinitionType.Assembly.GetExportedTypes()
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                .ShouldNotContain(p => p.PropertyType.Name.StartsWith("ComponentContract", StringComparison.Ordinal));
            var registration = family.DefinitionType.Assembly.GetExportedTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.ReturnType == typeof(IServiceCollection) && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(IServiceCollection))
                .ShouldHaveSingleItem();
            registration.Invoke(null, new object[] { services });
            registration.Invoke(null, new object[] { services });
        }
        using var provider = services.BuildServiceProvider();
        FluxFlowRegistrationExtensions.CreateFactoryMap(provider).Count.ShouldBe(45);
    }

    [Fact]
    public void All_19_family_package_readmes_explain_code_first_contract_ownership_and_explicit_portable_registration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var packageReadmes = Directory
            .EnumerateFiles(sourceRoot, "README.md", SearchOption.AllDirectories)
            .ToArray();
        var resolved = DefinitionFamilies.Select(family =>
        {
            var assemblyName = family.DefinitionType.Assembly.GetName().Name;
            assemblyName.ShouldNotBeNull();
            var nonNullAssemblyName = assemblyName!;
            var readme = packageReadmes
                .Where(path => string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    nonNullAssemblyName,
                    StringComparison.Ordinal))
                .ShouldHaveSingleItem(
                    $"{nonNullAssemblyName} must own exactly one package README under src.");

            return (AssemblyName: nonNullAssemblyName, Readme: readme);
        }).ToArray();

        resolved.Length.ShouldBe(19);
        resolved.Select(static item => item.Readme)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ShouldBe(19);

        var mqttAssemblyName = typeof(MqttApplicationDefinitionExtensions).Assembly.GetName().Name;
        mqttAssemblyName.ShouldNotBeNull();
        var nonNullMqttAssemblyName = mqttAssemblyName!;
        var mqttReadme = resolved.Single(item => item.AssemblyName == nonNullMqttAssemblyName).Readme;
        Path.GetRelativePath(root, mqttReadme).Replace('\\', '/').ShouldBe(
            $"src/Components/Mqtt/{nonNullMqttAssemblyName}/README.md");

        foreach (var family in resolved)
        {
            var relativeReadme = Path.GetRelativePath(root, family.Readme).Replace('\\', '/');
            var normalized = string.Join(
                ' ',
                File.ReadAllText(family.Readme)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            normalized.ShouldContain("ApplicationSetup.ComponentFactories", Case.Sensitive);
            normalized.ShouldContain("AddFluxFlowUpdates", Case.Sensitive);
            normalized.ShouldContain("same callback", Case.Sensitive);
        }
    }

    private static bool IsWorkflowExtension(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length > 0 &&
               parameters[0].ParameterType == typeof(WorkflowDefinitionBuilder);
    }

    private static bool HasOutParameter(MethodInfo method)
        => method.GetParameters().Any(static parameter => parameter.IsOut);

    private static bool HasOptionalConfigure(MethodInfo method)
    {
        var parameter = method.GetParameters()[^1];
        return parameter.IsOptional &&
               parameter.ParameterType.IsGenericType &&
               parameter.ParameterType.GetGenericTypeDefinition() == typeof(Action<>);
    }

    private static bool MatchesCapture(
        MethodInfo candidate,
        MethodInfo original,
        bool omitConfigure)
    {
        if (!string.Equals(candidate.Name, original.Name, StringComparison.Ordinal))
            return false;

        var originalParameters = original.GetParameters();
        var inputCount = omitConfigure ? originalParameters.Length - 1 : originalParameters.Length;
        var candidateParameters = candidate.GetParameters();
        if (candidateParameters.Length != inputCount + 1)
            return false;

        for (var index = 0; index < inputCount; index++)
        {
            if (candidateParameters[index].ParameterType != originalParameters[index].ParameterType)
                return false;
        }

        var capture = candidateParameters[^1];
        return capture.IsOut && capture.ParameterType == original.ReturnType.MakeByRefType();
    }

    private static void AssertCaptureShape(MethodInfo capture, Type handleType)
    {
        capture.ReturnType.ShouldBe(typeof(WorkflowDefinitionBuilder));
        capture.GetParameters()[0].ParameterType.ShouldBe(typeof(WorkflowDefinitionBuilder));
        var output = capture.GetParameters()[^1];
        output.IsOut.ShouldBeTrue();
        output.ParameterType.ShouldBe(handleType.MakeByRefType());
    }

    private sealed record FamilyCase(Type DefinitionType, string[] MethodNames);
}
