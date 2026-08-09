using System.Reflection;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class CodeFirstApplicationAuthoringConventionTests
{
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

        var contracts = typeof(ApplicationDefinition)
            .GetProperty(nameof(ApplicationDefinition.ApplicationResourceContracts))!;
        contracts.PropertyType.ShouldBe(
            typeof(IReadOnlyList<ApplicationResourceContract>));
        contracts.GetMethod!.IsPublic.ShouldBeTrue();
        contracts.SetMethod.ShouldBeNull();

        var root = ReleaseTestPaths.FindRepositoryRoot();
        var applicationPorts = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FluxFlow.Engine",
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

        var normalRegistrationMethods = typeof(FluxFlowRegistrationBuilder).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        normalRegistrationMethods.ShouldNotContain(static method =>
            method.Name == "AddRuntimeComponent" ||
            method.Name == "AddDynamicComponent");
        typeof(FluxFlowRegistrationBuilder).GetProperty(nameof(FluxFlowRegistrationBuilder.Advanced))!
            .PropertyType.ShouldBe(typeof(AdvancedFluxFlowRegistrationBuilder));
        var dynamicMethod = typeof(AdvancedFluxFlowRegistrationBuilder).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ShouldHaveSingleItem();
        dynamicMethod.Name.ShouldBe(nameof(AdvancedFluxFlowRegistrationBuilder.AddDynamicComponent));
        dynamicMethod.ReturnType.ShouldBe(typeof(AdvancedFluxFlowRegistrationBuilder));
        dynamicMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([
                typeof(string),
                typeof(Action<RuntimeComponentRegistrationBuilder>)
            ]);
    }

    [Fact]
    public void Typed_code_first_public_surface_matches_the_complete_contract()
    {
        var assembly = typeof(ApplicationDefinitionBuilder).Assembly;
        var factoryType = typeof(ComponentContract);
        var simpleContract = typeof(ComponentContract<>);
        var configuredContract = typeof(ComponentContract<,>);

        factoryType.IsAbstract.ShouldBeTrue();
        factoryType.IsSealed.ShouldBeFalse();
        factoryType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();
        factoryType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldHaveSingleItem().IsFamilyAndAssembly.ShouldBeTrue();
        AssertContractShape(simpleContract, handleParameterIndex: 0);
        AssertContractShape(configuredContract, handleParameterIndex: 1);
        assembly.GetType(
                "FluxFlow.Composition.Authoring.ComponentAuthoringContract",
                throwOnError: false)
            .ShouldBeNull();
        assembly.GetType(
                "FluxFlow.Composition.Authoring.ComponentAuthoringContract`1",
                throwOnError: false)
            .ShouldBeNull();
        assembly.GetType(
                "FluxFlow.Composition.Authoring.ComponentAuthoringContract`2",
                throwOnError: false)
            .ShouldBeNull();
        factoryType.GetProperty(nameof(ComponentContract.Type))!.SetMethod.ShouldBeNull();
        factoryType.GetProperty(nameof(ComponentContract.Descriptor))!.PropertyType
            .ShouldBe(typeof(ComponentDescriptor));
        factoryType.GetProperty(nameof(ComponentContract.Descriptor))!.SetMethod.ShouldBeNull();
        var customHandleConstructor = typeof(AuthoredComponentHandle)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldHaveSingleItem();
        customHandleConstructor.IsFamily.ShouldBeTrue(
            "application and family assemblies must be able to define meaningful custom handles.");
        customHandleConstructor.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(ComponentHandle)]);
        var rawDefinition = typeof(AuthoredComponentHandle).GetProperty("Definition")!;
        rawDefinition.PropertyType.ShouldBe(typeof(ComponentHandle));
        rawDefinition.GetMethod!.IsPublic.ShouldBeTrue();
        rawDefinition.SetMethod.ShouldBeNull();

        var createMethods = factoryType.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == "Create")
            .OrderBy(static method => method.GetGenericArguments().Length)
            .ToArray();
        createMethods.Length.ShouldBe(2);
        AssertCreateShape(createMethods[0], configured: false);
        AssertCreateShape(createMethods[1], configured: true);

        var componentDescriptors = typeof(ApplicationDefinition)
            .GetProperty(nameof(ApplicationDefinition.ComponentDescriptors))!;
        componentDescriptors.PropertyType.ShouldBe(typeof(IReadOnlyList<ComponentDescriptor>));
        componentDescriptors.GetMethod!.IsPublic.ShouldBeTrue();
        componentDescriptors.SetMethod.ShouldBeNull();
        var typedAddComponents = typeof(WorkflowDefinitionBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == nameof(WorkflowDefinitionBuilder.AddComponent))
            .Where(static method => method.GetParameters().Any(parameter =>
                parameter.ParameterType.IsGenericType &&
                (parameter.ParameterType.GetGenericTypeDefinition() == typeof(ComponentContract<>) ||
                 parameter.ParameterType.GetGenericTypeDefinition() == typeof(ComponentContract<,>))))
            .ToArray();
        typedAddComponents.Length.ShouldBe(4);

        var links = typeof(ApplicationDefinition).GetProperty("Links")!;
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
            "IsConditional",
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
        linkType.GetProperty("DeclarationSide")!.PropertyType
            .ShouldBe(typeof(ApplicationLinkDeclarationSide));

        AssertConnectionTriplets(typeof(OutputPortHandle<>), "ConnectTo", direct: true);
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
        var graphAuthoringMethods = publicTypes
            .Where(static type => IsCodeFirstGraphSurface(type))
            .SelectMany(static type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .ToArray();

        graphAuthoringMethods.ShouldNotContain(static method =>
            method.Name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Serialize", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Export", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Designer", StringComparison.OrdinalIgnoreCase));
        graphAuthoringMethods.ShouldNotContain(static method =>
            method.ReturnType == typeof(Task) ||
            method.ReturnType == typeof(ValueTask) ||
            method.ReturnType.IsGenericType &&
            (method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)));
        graphAuthoringMethods
            .SelectMany(static method => method.GetParameters())
            .ShouldNotContain(static parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(System.Linq.Expressions.Expression<>));
        typeof(ApplicationDefinitionBuilder)
            .GetMethod(nameof(ApplicationDefinitionBuilder.Build), Type.EmptyTypes)!
            .ReturnType.ShouldBe(typeof(ApplicationDefinition));
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
        source.ShouldContain(".AddComponent(");
        source.ShouldContain(".ConnectTo(");
        source.ShouldNotContain("ApplicationDefinitionJson");
        source.ShouldNotContain("JsonDocument");
        source.ShouldNotContain("new ComponentDefinition(");
        source.ShouldNotContain(".Input<");
        source.ShouldNotContain(".Output<");
        source.ShouldNotContain("ApplicationAddress.Parse(");
    }

    [Fact]
    public void Designer_source_uses_only_the_explicit_contract_wrapper_without_graph_authoring_or_csharp_export()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var designerRoots = new[]
        {
            Path.Combine(root, "src", "FluxFlow.Components.Designer"),
            Path.Combine(root, "src", "FluxFlow.Designer"),
            Path.Combine(root, "src", "FluxFlow.Designer.Abstractions")
        };
        var sources = designerRoots
            .Where(Directory.Exists)
            .SelectMany(static directory =>
                Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(static path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        sources.ShouldNotBeEmpty();
        var combined = string.Join(Environment.NewLine, sources.Select(File.ReadAllText));
        combined.ShouldContain("public static class DesignedComponentContract");
        combined.ShouldContain("ComponentContract<THandle>");
        combined.ShouldContain("ComponentContract<TOptions, THandle>");
        combined.ShouldContain("IDesignedComponentContract");
        combined.ShouldNotContain("ApplicationDefinitionBuilder");
        combined.ShouldNotContain("ComponentAuthoringContract");
        combined.ShouldNotContain("ConnectTo(");
        combined.ShouldNotContain("ExportCSharp");
        combined.ShouldNotContain("ExportCode");
    }

    private static void AssertContractShape(Type contractType, int handleParameterIndex)
    {
        contractType.IsPublic.ShouldBeTrue();
        contractType.IsSealed.ShouldBeFalse();
        contractType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
        contractType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldHaveSingleItem().IsFamilyOrAssembly.ShouldBeTrue();
        var handleParameter = contractType.GetGenericArguments()[handleParameterIndex];
        handleParameter.GetGenericParameterConstraints()
            .ShouldContain(typeof(AuthoredComponentHandle));
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

    private static void AssertCreateShape(MethodInfo method, bool configured)
    {
        method.IsGenericMethodDefinition.ShouldBeTrue();
        var genericArguments = method.GetGenericArguments();
        genericArguments.Length.ShouldBe(configured ? 2 : 1);
        var optionsType = configured ? genericArguments[0] : null;
        var handleType = genericArguments[^1];
        handleType.GetGenericParameterConstraints()
            .ShouldContain(typeof(AuthoredComponentHandle));

        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(configured ? 5 : 3);
        parameters[0].Name.ShouldBe("type");
        parameters[0].ParameterType.ShouldBe(typeof(string));
        var index = 1;
        parameters[index].Name.ShouldBe("configureRuntime");
        parameters[index++].ParameterType.ShouldBe(
            typeof(Action<>).MakeGenericType(typeof(RuntimeComponentRegistrationBuilder)));
        if (configured)
        {
            parameters[index].Name.ShouldBe("createOptions");
            parameters[index++].ParameterType.ShouldBe(typeof(Func<>).MakeGenericType(optionsType!));
            parameters[index].Name.ShouldBe("apply");
            parameters[index++].ParameterType.ShouldBe(
                typeof(Action<,>).MakeGenericType(optionsType!, typeof(ComponentDefinitionBuilder)));
        }

        parameters[index].Name.ShouldBe("createHandle");
        parameters[index].ParameterType.ShouldBe(
            typeof(Func<,>).MakeGenericType(typeof(ComponentHandle), handleType));
        method.ReturnType.GetGenericTypeDefinition().ShouldBe(
            configured
                ? typeof(ComponentContract<,>)
                : typeof(ComponentContract<>));
        method.ReturnType.GetGenericArguments().ShouldBe(
            configured
                ? [optionsType!, handleType]
                : [handleType]);
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
                .ThenBy(static method => method.GetParameters()[^1].ParameterType == typeof(string) ? 0 : 1)
                .ToArray();

            targetMethods.Length.ShouldBe(3);
            targetMethods[0].GetParameters().Length.ShouldBe(direct ? 1 : 2);
            targetMethods[1].GetParameters()[^1].Name.ShouldBe("condition");
            targetMethods[1].GetParameters()[^1].ParameterType.ShouldBe(typeof(string));
            targetMethods[1].GetParameters()[^1].IsOptional.ShouldBeFalse();
            targetMethods[2].GetParameters()[^1].Name.ShouldBe("when");
            targetMethods[2].GetParameters()[^1].ParameterType.IsGenericType.ShouldBeTrue();
            targetMethods[2].GetParameters()[^1].ParameterType.GetGenericTypeDefinition()
                .ShouldBe(typeof(Func<,>));
            targetMethods[2].GetParameters()[^1].IsOptional.ShouldBeFalse();
            targetMethods.ShouldAllBe(method =>
                method.ReturnType == (direct ? typeof(OutputPortHandle<>) : type) ||
                direct && method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(OutputPortHandle<>));
        }
    }

    private static bool IsTarget(Type actual, Type expected)
        => expected.IsGenericTypeDefinition
            ? actual.IsGenericType && actual.GetGenericTypeDefinition() == expected
            : actual == expected;
}
