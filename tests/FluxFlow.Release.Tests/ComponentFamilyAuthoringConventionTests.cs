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
using FluxFlow.Composition.Authoring;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ComponentFamilyAuthoringConventionTests
{
    private static readonly FamilyCase[] AuthoringFamilies =
    [
        new(typeof(AssertionsAuthoringExtensions), ["AddAssertion"]),
        new(typeof(ExpectationsAuthoringExtensions), ["AddEventExpectation"]),
        new(
            typeof(FileSystemAuthoringExtensions),
            ["AddDirectoryEnumerate", "AddFileRead", "AddFileWatch", "AddFileWrite"]),
        new(typeof(HttpAuthoringExtensions), ["AddHttpRequest"]),
        new(typeof(MappingAuthoringExtensions), ["AddMapper"]),
        new(typeof(MetricsAuthoringExtensions), ["AddMetricAggregation"]),
        new(
            typeof(MqttApplicationAuthoringExtensions),
            ["AddMqttCommand", "AddMqttEvents", "AddMqttPublish", "AddMqttReceive"]),
        new(
            typeof(ObservabilityAuthoringExtensions),
            ["AddCounter", "AddLogger", "AddMetrics"]),
        new(typeof(PayloadsAuthoringExtensions), ["AddPayloadInspection"]),
        new(typeof(ProjectionsAuthoringExtensions), ["AddEventProjection"]),
        new(typeof(ResilienceAuthoringExtensions), ["AddFlowRetry"]),
        new(
            typeof(RoutingAuthoringExtensions),
            ["AddCorrelation", "AddJoin", "AddWindow"]),
        new(
            typeof(SerializationAuthoringExtensions),
            [
                "AddBase64Decode",
                "AddBase64Encode",
                "AddJsonParse",
                "AddJsonStringify",
                "AddTextDecode",
                "AddTextEncode"
            ]),
        new(
            typeof(SessionsAuthoringExtensions),
            ["AddSessionQuery", "AddSessionRecorder", "AddSessionReplay"]),
        new(
            typeof(SourcesAuthoringExtensions),
            ["AddGeneratedSource", "AddSequenceSource"]),
        new(typeof(StateAuthoringExtensions), ["AddStateReducer"]),
        new(
            typeof(StorageAuthoringExtensions),
            ["AddStorageDelete", "AddStorageGet", "AddStoragePut", "AddStorageQuery"]),
        new(
            typeof(TimersAuthoringExtensions),
            ["AddDebounce", "AddDelay", "AddIntervalTimer", "AddScheduleTimer", "AddThrottle"]),
        new(typeof(ValidationAuthoringExtensions), ["AddJsonSchemaValidator"])
    ];

    [Fact]
    public void Explicit_19_family_matrix_exposes_complete_fluent_capture_convention()
    {
        AuthoringFamilies.Length.ShouldBe(19);
        AuthoringFamilies.Select(static family => family.AuthoringType.Assembly)
            .Distinct()
            .Count()
            .ShouldBe(19);

        var originalCount = 0;
        var optionalConfigureCount = 0;
        var fluentCount = 0;

        foreach (var family in AuthoringFamilies)
        {
            var methods = family.AuthoringType
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
                        $"{family.AuthoringType.Name}.{original.Name} must have one exact configured capture overload.");
                AssertCaptureShape(configuredCapture, original.ReturnType);

                if (!HasOptionalConfigure(original))
                    continue;

                var noConfigurationCapture = fluent
                    .Where(candidate => MatchesCapture(candidate, original, omitConfigure: true))
                    .ShouldHaveSingleItem(
                        $"{family.AuthoringType.Name}.{original.Name} must have one exact no-configuration capture overload.");
                AssertCaptureShape(noConfigurationCapture, original.ReturnType);
                optionalConfigureCount++;
            }

            fluent.Length.ShouldBe(
                originals.Length + originals.Count(HasOptionalConfigure),
                $"{family.AuthoringType.Name} has an unmatched or missing fluent capture overload.");
            originalCount += originals.Length;
            fluentCount += fluent.Length;
        }

        originalCount.ShouldBe(44);
        optionalConfigureCount.ShouldBe(11);
        fluentCount.ShouldBe(55);
    }

    [Fact]
    public void Explicit_19_family_matrix_exposes_44_complete_contracts_with_exact_descriptors_and_events()
    {
        AuthoringFamilies.Length.ShouldBe(19);
        var contractCount = 0;

        foreach (var family in AuthoringFamilies)
        {
            var originals = family.AuthoringType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method =>
                    method.ReturnType != typeof(WorkflowDefinitionBuilder) &&
                    IsWorkflowExtension(method) &&
                    !HasOutParameter(method))
                .ToArray();
            var expectedProperties = family.MethodNames
                .Select(static methodName => methodName["Add".Length..])
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            var contractContainer = family.AuthoringType.Assembly
                .GetExportedTypes()
                .Where(static type => type.IsAbstract && type.IsSealed)
                .Where(type => expectedProperties.All(propertyName =>
                    type.GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) is not null))
                .ShouldHaveSingleItem(
                    $"{family.AuthoringType.Assembly.GetName().Name} must expose one public static contract container.");
            var properties = contractContainer.GetProperties(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray();

            contractContainer.Name.ShouldEndWith("Components");
            properties.Select(static property => property.Name).ShouldBe(expectedProperties);
            properties.Length.ShouldBe(originals.Length);

            foreach (var original in originals)
            {
                var propertyName = original.Name["Add".Length..];
                var property = properties
                    .Where(candidate => candidate.Name == propertyName)
                    .ShouldHaveSingleItem();
                property.GetMethod!.IsPublic.ShouldBeTrue();
                property.GetMethod.IsStatic.ShouldBeTrue();
                property.SetMethod.ShouldBeNull();
                var contractType = property.PropertyType;
                contractType.IsGenericType.ShouldBeTrue();
                var contractDefinition = contractType.GetGenericTypeDefinition();
                (contractDefinition == typeof(ComponentContract<>) ||
                 contractDefinition == typeof(ComponentContract<,>)).ShouldBeTrue(
                    $"{contractContainer.Name}.{propertyName} must expose a typed component contract.");
                var handleType = contractType.GetGenericArguments()[^1];
                handleType.ShouldBe(original.ReturnType);
                typeof(AuthoredComponentHandle).IsAssignableFrom(handleType).ShouldBeTrue();
                var events = handleType.GetProperty(
                    "Events",
                    BindingFlags.Public | BindingFlags.Instance);
                events.ShouldNotBeNull(
                    $"{contractContainer.Name}.{propertyName} handle must expose explicit Events.");
                var eventsProperty = events!;
                eventsProperty.PropertyType.ShouldBe(typeof(OutputPortHandle<ComponentEvent>));
                eventsProperty.GetMethod!.IsPublic.ShouldBeTrue();
                eventsProperty.SetMethod.ShouldBeNull();
                var contract = property.GetValue(null)
                    .ShouldBeAssignableTo<ComponentContract>();
                var completeContract = contract!;
                completeContract.Type.ShouldNotBeNullOrWhiteSpace();
                completeContract.Type.ShouldBe(completeContract.Type.Trim());
                completeContract.Descriptor.Type.ShouldBe(completeContract.Type);
                completeContract.Descriptor.Outputs["Events"].MessageType
                    .ShouldBe(typeof(ComponentEvent));
                completeContract.Descriptor.Outputs["Events"].Kind
                    .ShouldBe(ComponentPortKind.Message);
                completeContract.Descriptor.Inputs.Values.ShouldAllBe(static port =>
                    !string.IsNullOrWhiteSpace(port.Name) && port.MessageType != null);
                completeContract.Descriptor.Outputs.Values.ShouldAllBe(static port =>
                    !string.IsNullOrWhiteSpace(port.Name) && port.MessageType != null);
                completeContract.Descriptor.Options.Values.ShouldAllBe(static option =>
                    !string.IsNullOrWhiteSpace(option.Name) && option.ValueType != null);
                completeContract.Descriptor.Resources.Values.ShouldAllBe(static resource =>
                    !string.IsNullOrWhiteSpace(resource.Name) && resource.ServiceType != null);
                contractCount++;
            }
        }

        contractCount.ShouldBe(44);
    }

    [Fact]
    public void All_19_family_package_readmes_explain_code_first_descriptor_ownership_and_explicit_portable_registration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var packageReadmes = Directory
            .EnumerateFiles(sourceRoot, "README.md", SearchOption.AllDirectories)
            .ToArray();
        var resolved = AuthoringFamilies.Select(family =>
        {
            var assemblyName = family.AuthoringType.Assembly.GetName().Name;
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

        var mqttAssemblyName = typeof(MqttApplicationAuthoringExtensions).Assembly.GetName().Name;
        mqttAssemblyName.ShouldNotBeNull();
        var nonNullMqttAssemblyName = mqttAssemblyName!;
        var mqttReadme = resolved.Single(item => item.AssemblyName == nonNullMqttAssemblyName).Readme;
        Path.GetRelativePath(root, mqttReadme).Replace('\\', '/').ShouldBe(
            $"src/Mqtt/{nonNullMqttAssemblyName}/README.md");

        foreach (var family in resolved)
        {
            var relativeReadme = Path.GetRelativePath(root, family.Readme).Replace('\\', '/');
            var normalized = string.Join(
                ' ',
                File.ReadAllText(family.Readme)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            (normalized.Contains("A definition built from", StringComparison.Ordinal) &&
             normalized.Contains("retains its executable descriptor", StringComparison.Ordinal) &&
             normalized.Contains("complete contract", StringComparison.Ordinal))
                .ShouldBeTrue(
                    $"{relativeReadme} must state that complete code-first definitions retain executable descriptors.");
            normalized.Contains(
                    "Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and does not repeat the family registration",
                    StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"{relativeReadme} must state the single normal code-first registration boundary.");
            normalized.Contains(
                    "Use that service registration for JSON/configuration, catalog, or dynamic definitions",
                    StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"{relativeReadme} must reserve explicit family registration for portable or dynamic definitions.");
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

    private sealed record FamilyCase(Type AuthoringType, string[] MethodNames);
}
