using System.Text.Json;
using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DirectComponentFamilyTests
{
    [Theory]
    [InlineData("assertions", false)]
    [InlineData("assertions", true)]
    [InlineData("payloads", false)]
    [InlineData("payloads", true)]
    [InlineData("validation", false)]
    [InlineData("validation", true)]
    public async Task Typed_definition_and_DI_share_one_factory(string family, bool registerFamily)
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var services = new ServiceCollection();
        InputOutputComponentHandle<FlowValue, FlowValue> node;
        if (family == "assertions")
        {
            var engine = builder.AddResource<IFlowExpressionEngine>("engine", "external");
            services.AddExternalFluxFlowResource<IFlowExpressionEngine>(ApplicationAddress.Resource("engine"), new PassingEngine());
            node = workflow.AddAssertion("node", options => { options.Expression = "true"; options.Engine = engine; });
            if (registerFamily) services.AddAssertions();
        }
        else if (family == "payloads")
        {
            node = workflow.AddPayloadInspection("node");
            if (registerFamily) services.AddPayloads();
        }
        else
        {
            node = workflow.AddJsonSchemaValidator("node", options => options.Schema = JsonSerializer.SerializeToElement(new { type = "object" }));
            if (registerFamily) services.AddValidation();
        }
        node.Events.DiscardUnmatched();
        var setup = builder.Build();
        setup.ComponentFactories.Count.ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await setup.BuildApplicationAsync(provider);
        await app.StartAsync();
        var response = await app.RequestAsync("main.node.Input", "main.node.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { value = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        response.IsError.ShouldBeFalse();
        response.Value.ShouldNotBeNull();
        await app.StopAsync();
    }

    private sealed class PassingEngine : IFlowExpressionEngine
    {
        public string Name => "test";
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => true;
    }
}
