using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Json.Links;
using System.Reflection;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Core;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class CodeFirstApplicationDefinitionConventionTests
{
    [Fact]
    public void Port_link_names_are_symmetric_and_preserve_condition_overloads()
    {
        var outputs = typeof(OutputPortHandle<>).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        outputs.Select(static method => method.Name).Order(StringComparer.Ordinal)
            .ShouldBe(new[] { "DiscardUnmatched" }.Concat(Enumerable.Repeat("LinkTo", 6))
                .Concat(Enumerable.Repeat("OtherwiseTo", 2)));
        var discard = outputs.Single(method => method.Name == "DiscardUnmatched");
        discard.GetParameters().ShouldBeEmpty();
        discard.ReturnType.ShouldBe(typeof(OutputPortHandle<>));
        AssertConnectionTriplets(typeof(OutputPortHandle<>), "LinkTo", direct: true);

        var inputs = typeof(InputPortConnectionExtensions).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        inputs.Select(static method => method.Name).Order(StringComparer.Ordinal)
            .ShouldBe(Enumerable.Repeat("LinkFrom", 6).Concat(Enumerable.Repeat("OtherwiseFrom", 2)));
        foreach (var targetKind in new[] { typeof(InputPortHandle<>), typeof(SignalInputPortHandle) })
        {
            var overloads = inputs.Where(method => method.Name == "LinkFrom" &&
                    IsTarget(method.GetParameters()[0].ParameterType, targetKind))
                .ToArray();
            overloads.Length.ShouldBe(3);
            foreach (var method in overloads)
            {
                method.IsGenericMethodDefinition.ShouldBeTrue();
                var payload = method.GetGenericArguments().ShouldHaveSingleItem();
                var parameters = method.GetParameters();
                parameters[0].Name.ShouldBe("target");
                method.ReturnType.ShouldBe(parameters[0].ParameterType);
                parameters[1].Name.ShouldBe("source");
                parameters[1].ParameterType.ShouldBe(typeof(OutputPortHandle<>).MakeGenericType(payload));
                AssertDeliveryParameter(parameters[^1]);
            }
            overloads.Count(static method => method.GetParameters().Length == 3).ShouldBe(1);
            var expression = overloads.Single(static method => method.GetParameters()[^2].Name == "condition");
            expression.GetParameters().Length.ShouldBe(4);
            expression.GetParameters()[2].ParameterType.ShouldBe(typeof(string));
            var predicate = overloads.Single(static method => method.GetParameters()[^2].Name == "when");
            predicate.GetParameters().Length.ShouldBe(4);
            predicate.GetParameters()[2].ParameterType.ShouldBe(
                typeof(Func<,>).MakeGenericType(predicate.GetGenericArguments()[0], typeof(bool)));

            var fallbackFrom = inputs.Single(method => method.Name == "OtherwiseFrom" &&
                IsTarget(method.GetParameters()[0].ParameterType, targetKind));
            fallbackFrom.IsGenericMethodDefinition.ShouldBeTrue();
            var fallbackPayload = fallbackFrom.GetGenericArguments().ShouldHaveSingleItem();
            var fromParameters = fallbackFrom.GetParameters();
            fromParameters.Length.ShouldBe(3);
            fromParameters.Select(parameter => parameter.Name).ShouldBe(["target", "source", "delivery"]);
            fallbackFrom.ReturnType.ShouldBe(fromParameters[0].ParameterType);
            fromParameters[1].ParameterType.ShouldBe(typeof(OutputPortHandle<>).MakeGenericType(fallbackPayload));
            AssertDeliveryParameter(fromParameters[2]);
            var fallbackTo = outputs.Single(method => method.Name == "OtherwiseTo" &&
                IsTarget(method.GetParameters()[0].ParameterType, targetKind));
            fallbackTo.ReturnType.ShouldBe(typeof(OutputPortHandle<>));
            var toParameters = fallbackTo.GetParameters();
            toParameters.Length.ShouldBe(2);
            toParameters.Select(parameter => parameter.Name).ShouldBe(["target", "delivery"]);
            AssertDeliveryParameter(toParameters[1]);
        }
    }

    [Fact]
    public void Executable_resource_typed_port_and_advanced_registration_surfaces_are_explicit()
    {
        var resourceFactory = typeof(ApplicationResourceContract);
        var simpleResourceContract = typeof(ApplicationResourceContract<>);
        var configuredResourceContract = typeof(ApplicationResourceContract<,>);

        resourceFactory.IsAbstract.ShouldBeTrue();
        resourceFactory.IsSealed.ShouldBeFalse();
        resourceFactory.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();
        resourceFactory.GetProperty(nameof(ApplicationResourceContract.Type))!
            .SetMethod.ShouldBeNull();
        resourceFactory.GetProperty("Registrar", BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeNull();
        AssertResourceContractShape(simpleResourceContract, handleParameterIndex: 0);
        AssertResourceContractShape(configuredResourceContract, handleParameterIndex: 1);

        var createMethods = resourceFactory.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == nameof(ApplicationResourceContract.Create))
            .OrderBy(static method => method.GetGenericArguments().Length)
            .ToArray();
        createMethods.Length.ShouldBe(2);
        AssertResourceCreateShape(createMethods[0], configured: false);
        AssertResourceCreateShape(createMethods[1], configured: true);

        var customHandleConstructor = typeof(AuthoredResourceHandle)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldHaveSingleItem();
        customHandleConstructor.IsFamily.ShouldBeTrue();
        customHandleConstructor.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(ResourceHandle)]);
        var rawDefinition = typeof(AuthoredResourceHandle)
            .GetProperty(nameof(AuthoredResourceHandle.Definition))!;
        rawDefinition.PropertyType.ShouldBe(typeof(ResourceHandle));
        rawDefinition.GetMethod!.IsPublic.ShouldBeTrue();
        rawDefinition.SetMethod.ShouldBeNull();

        var contracts = typeof(ApplicationSetup)
            .GetProperty(nameof(ApplicationSetup.ResourceContracts))!;
        contracts.PropertyType.ShouldBe(
            typeof(IReadOnlyList<ApplicationResourceContract>));
        contracts.GetMethod!.IsPublic.ShouldBeTrue();
        contracts.SetMethod.ShouldBeNull();

        var root = ReleaseTestPaths.FindRepositoryRoot();
        var applicationPorts = File.ReadAllText(Path.Combine(
            root,
            "src", "Engine", "Runtime", "FluxFlow.Engine",
            "ApplicationPorts.cs"));
        applicationPorts.ShouldContain("InputPortHandle<T> input");
        applicationPorts.ShouldContain("SignalInputPortHandle input");
        applicationPorts.ShouldContain("OutputPortHandle<T> output");
        applicationPorts.ShouldContain("InputPortHandle<TRequest> input");
        applicationPorts.ShouldContain("OutputPortHandle<TResponse> output");
        applicationPorts.ShouldContain("return SendAsync(input.Address");
        applicationPorts.ShouldContain("return ReceiveAsync<T>(output.Address");
        applicationPorts.ShouldContain("return ObserveAsync<T>(output.Address");
        applicationPorts.ShouldNotContain("public ValueTask<IAsyncDisposable> Attach");
        applicationPorts.ShouldNotContain("public IDisposable Attach");

        typeof(FluxFlowRegistrationExtensions).Assembly.GetType("FluxFlow.Composition.FluxFlowRegistrationBuilder").ShouldBeNull();
        var method = typeof(FluxFlowRegistrationExtensions).GetMethods().First(method => method.Name == "AddComponent" && method.GetParameters().Length == 3);
        method.ReturnType.ShouldBe(typeof(IServiceCollection));
        method.GetParameters().Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(IServiceCollection), typeof(string), typeof(Func<ComponentActivationContext, FluxFlow.Nodes.IFlowNode>)]);

    }

    [Fact]
    public void Code_first_public_surface_uses_factories_without_component_contracts()
    {
        var assembly = typeof(ApplicationDefinitionBuilder).Assembly;
        foreach (var name in new[] { "ComponentContract", "ComponentContract`1", "ComponentContract`2", "ComponentContractCollection" })
            assembly.GetType("FluxFlow.Composition.Definition." + name).ShouldBeNull();
        assembly.GetType("FluxFlow.Composition.ComponentNodeActivation`1").ShouldBeNull();
        typeof(ApplicationSetup).GetProperty("ComponentContracts").ShouldBeNull();
        typeof(ApplicationSetup).GetProperty(nameof(ApplicationSetup.ComponentFactories))!.SetMethod.ShouldBeNull();
        var links = typeof(ApplicationSetup).GetProperty("Links")!;
        links.CanRead.ShouldBeTrue();
        links.SetMethod.ShouldBeNull();
        links.PropertyType.IsGenericType.ShouldBeTrue();
        links.PropertyType.GetGenericTypeDefinition().ShouldBe(typeof(IReadOnlyList<>));
        var linkType = links.PropertyType.GetGenericArguments().ShouldHaveSingleItem();
        linkType.Name.ShouldBe("ApplicationLinkDefinition");
        linkType.IsPublic.ShouldBeTrue();
        linkType.IsSealed.ShouldBeTrue();
        linkType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();

        var linkProperties = linkType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        linkProperties.Select(static property => property.Name).ShouldBe(
        [
            "ConditionExpression",
            "DeclarationSide",
            "Delivery",
            "IsConditional",
            "IsFallback",
            "MessageType",
            "Source",
            "Target"
        ]);
        linkProperties.ShouldAllBe(static property =>
            property.CanRead && property.SetMethod == null);
        linkType.GetProperty("Source")!.PropertyType.ShouldBe(typeof(ApplicationAddress));
        linkType.GetProperty("Target")!.PropertyType.ShouldBe(typeof(ApplicationAddress));
        linkType.GetProperty("MessageType")!.PropertyType.ShouldBe(typeof(Type));
        linkType.GetProperty("ConditionExpression")!.PropertyType.ShouldBe(typeof(string));
        linkType.GetProperty("IsConditional")!.PropertyType.ShouldBe(typeof(bool));
        linkType.GetProperty("IsFallback")!.PropertyType.ShouldBe(typeof(bool));
        linkType.GetProperty("Delivery")!.PropertyType.ShouldBe(typeof(FlowDeliveryPolicy));
        linkType.GetProperty("DeclarationSide")!.PropertyType
            .ShouldBe(typeof(ApplicationLinkDeclarationSide));

        AssertConnectionTriplets(typeof(OutputPortHandle<>), "LinkTo", direct: true);
        AssertConnectionTriplets(typeof(WorkflowDefinitionBuilder), "Connect", direct: false);
        AssertConnectionTriplets(typeof(ApplicationDefinitionBuilder), "Connect", direct: false);
    }

    [Fact]
    public void Code_first_builder_public_surface_has_no_serialization_export_designer_or_async_predicate_API()
    {
        var publicTypes = typeof(ApplicationDefinitionBuilder).Assembly
            .GetExportedTypes()
            .Where(static type => type.Namespace is not null &&
                type.Namespace.StartsWith("FluxFlow.Composition", StringComparison.Ordinal))
            .ToArray();
        var graphDefinitionMethods = publicTypes
            .Where(static type => IsCodeFirstGraphSurface(type))
            .SelectMany(static type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .ToArray();

        graphDefinitionMethods.ShouldNotContain(static method =>
            method.Name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Serialize", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Export", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Designer", StringComparison.OrdinalIgnoreCase));
        graphDefinitionMethods.ShouldNotContain(static method =>
            method.ReturnType == typeof(Task) ||
            method.ReturnType == typeof(ValueTask) ||
            method.ReturnType.IsGenericType &&
            (method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));
        graphDefinitionMethods
            .SelectMany(static method => method.GetParameters())
            .ShouldNotContain(static parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(System.Linq.Expressions.Expression<>));
        typeof(ApplicationDefinitionBuilder)
            .GetMethod(nameof(ApplicationDefinitionBuilder.Build), Type.EmptyTypes)!
            .ReturnType.ShouldBe(typeof(ApplicationSetup));
    }

    private static bool IsCodeFirstGraphSurface(Type type)
        => type.Namespace == typeof(ApplicationDefinitionBuilder).Namespace &&
           (type == typeof(ApplicationDefinitionBuilder) ||
            type == typeof(WorkflowDefinitionBuilder) ||
            type == typeof(ComponentHandle) ||
            type == typeof(AuthoredComponentHandle) ||
            type.Name.StartsWith("ComponentContract", StringComparison.Ordinal) ||
            type.Name.EndsWith("PortHandle", StringComparison.Ordinal));

    [Fact]
    public void Sample_workspace_uses_typed_contract_handles_without_raw_link_reconstruction()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "FluxFlow.SampleApp",
            "SampleWorkspaceDefinition.cs"));

        source.ShouldContain("new ApplicationDefinitionBuilder()");
        source.ShouldContain(".AddWorkflow(");
        source.ShouldContain(".AddOrderSource(");
        source.ShouldContain(".LinkTo(");
        source.ShouldNotContain("ApplicationDefinitionJson");
        source.ShouldNotContain("JsonDocument");
        source.ShouldNotContain("new ComponentDefinition(");
        source.ShouldNotContain(".Input<");
        source.ShouldNotContain(".Output<");
        source.ShouldNotContain("ApplicationAddress.Parse(");
    }

    private static void AssertResourceContractShape(Type contractType, int handleParameterIndex)
    {
        contractType.IsPublic.ShouldBeTrue();
        contractType.IsSealed.ShouldBeFalse();
        contractType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();
        contractType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldHaveSingleItem().IsFamilyOrAssembly.ShouldBeTrue();
        contractType.GetGenericArguments()[handleParameterIndex]
            .GetGenericParameterConstraints()
            .ShouldContain(typeof(AuthoredResourceHandle));
    }

    private static void AssertResourceCreateShape(MethodInfo method, bool configured)
    {
        method.IsGenericMethodDefinition.ShouldBeTrue();
        var genericArguments = method.GetGenericArguments();
        genericArguments.Length.ShouldBe(configured ? 2 : 1);
        var optionsType = configured ? genericArguments[0] : null;
        var handleType = genericArguments[^1];
        handleType.GetGenericParameterConstraints()
            .ShouldContain(typeof(AuthoredResourceHandle));

        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(configured ? 5 : 3);
        parameters[0].Name.ShouldBe("type");
        parameters[0].ParameterType.ShouldBe(typeof(string));
        parameters[1].Name.ShouldBe("registrar");
        parameters[1].ParameterType.ShouldBe(typeof(IApplicationResourceRegistrar));
        var index = 2;
        if (configured)
        {
            parameters[index].Name.ShouldBe("createOptions");
            parameters[index++].ParameterType.ShouldBe(
                typeof(Func<>).MakeGenericType(optionsType!));
            parameters[index].Name.ShouldBe("apply");
            parameters[index++].ParameterType.ShouldBe(
                typeof(Action<,>).MakeGenericType(optionsType!, typeof(ResourceDefinitionBuilder)));
        }

        parameters[index].Name.ShouldBe("createHandle");
        parameters[index].ParameterType.ShouldBe(
            typeof(Func<,>).MakeGenericType(typeof(ResourceHandle), handleType));
        method.ReturnType.GetGenericTypeDefinition().ShouldBe(
            configured
                ? typeof(ApplicationResourceContract<,>)
                : typeof(ApplicationResourceContract<>));
    }

    private static void AssertConnectionTriplets(Type type, string methodName, bool direct)
    {
        var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == methodName)
            .ToArray();

        methods.Length.ShouldBe(6);
        foreach (var targetKind in new[] { typeof(InputPortHandle<>), typeof(SignalInputPortHandle) })
        {
            var targetMethods = methods
                .Where(method => IsTarget(method.GetParameters()[direct ? 0 : 1].ParameterType, targetKind))
                .OrderBy(static method => method.GetParameters().Length)
                .ThenBy(static method => method.GetParameters()[^2].ParameterType == typeof(string) ? 0 : 1)
                .ToArray();

            targetMethods.Length.ShouldBe(3);
            foreach (var method in targetMethods) AssertDeliveryParameter(method.GetParameters()[^1]);
            targetMethods[0].GetParameters().Length.ShouldBe(direct ? 2 : 3);
            targetMethods[1].GetParameters()[^2].Name.ShouldBe("condition");
            targetMethods[1].GetParameters()[^2].ParameterType.ShouldBe(typeof(string));
            targetMethods[1].GetParameters()[^2].IsOptional.ShouldBeFalse();
            targetMethods[2].GetParameters()[^2].Name.ShouldBe("when");
            targetMethods[2].GetParameters()[^2].ParameterType.IsGenericType.ShouldBeTrue();
            targetMethods[2].GetParameters()[^2].ParameterType.GetGenericTypeDefinition()
                .ShouldBe(typeof(Func<,>));
            targetMethods[2].GetParameters()[^2].IsOptional.ShouldBeFalse();
            targetMethods.ShouldAllBe(method =>
                method.ReturnType == (direct ? typeof(OutputPortHandle<>) : type) ||
                direct && method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(OutputPortHandle<>));
        }
    }

    private static void AssertDeliveryParameter(ParameterInfo parameter)
    {
        parameter.Name.ShouldBe("delivery");
        parameter.ParameterType.ShouldBe(typeof(FlowDeliveryPolicy));
        parameter.IsOptional.ShouldBeTrue();
        parameter.DefaultValue.ShouldBe(FlowDeliveryPolicy.Reliable);
    }

    private static bool IsTarget(Type actual, Type expected)
        => expected.IsGenericTypeDefinition
            ? actual.IsGenericType && actual.GetGenericTypeDefinition() == expected
            : actual == expected;
}
