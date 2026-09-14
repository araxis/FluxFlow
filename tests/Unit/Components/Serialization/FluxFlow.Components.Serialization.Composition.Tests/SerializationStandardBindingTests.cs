using FluxFlow.Composition.Definition;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Composition.Tests;

public sealed class SerializationStandardBindingTests
{
    [Theory]
    [InlineData("json.parse")]
    [InlineData("json.stringify")]
    [InlineData("text.encode")]
    [InlineData("text.decode")]
    [InlineData("base64.encode")]
    [InlineData("base64.decode")]
    public async Task Serialization_standard_bindings_preserve_native_identity(string type)
    {
        var services = new ServiceCollection();
        services.AddSerialization();
        using var provider = services.BuildServiceProvider();
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[type](new ComponentActivationContext(
            provider, "main", "node", new ComponentDefinition(type)));
        instance.InputMetadata["Input"].MessageType.ShouldBe(typeof(FlowValue));
        instance.InputMetadata["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        var component = instance.Node.ShouldBeAssignableTo<IFlowComponent>().ShouldNotBeNull();
        try
        {
            instance.Inputs.Keys.ShouldBe(["Input"]);
            instance.Outputs.Keys.Order(StringComparer.Ordinal).ShouldBe(["Events", "Output"]);
            instance.Inputs["Input"].Target.ShouldBeSameAs(component.Ports.Inputs["Input"]);
            instance.Outputs["Output"].Source.ShouldBeSameAs(component.Ports.Outputs["Output"]);
            component.Ports.Events.ShouldNotBeNull();
            component.Ports.Controls.ShouldBeEmpty();
            component.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { await instance.DisposeAsync(); }
        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
