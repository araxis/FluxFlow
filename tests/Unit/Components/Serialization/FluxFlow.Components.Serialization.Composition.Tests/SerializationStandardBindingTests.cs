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
        services.AddFluxFlowComponents().AddSerialization();
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentCatalog>().Components[type];
        descriptor.Inputs.Keys.ShouldBe(["Input"]);
        descriptor.Outputs.Keys.Order(StringComparer.Ordinal).ShouldBe(["Events", "Output"]);
        descriptor.Inputs["Input"].InputShape.ShouldNotBeNull();
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(FlowValue));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        var instance = await descriptor.Factory(new ComponentActivationContext(
            provider, "main", "node", new ComponentDefinition(type)));
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
