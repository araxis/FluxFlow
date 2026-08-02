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
