using System.Text.Json;
using FluxFlow.Testing;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class WorkflowResourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_shared_and_cross_workflow_resources_have_one_application_lifetime(bool failBuild)
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
        {"Resources":{"Connections":{"store":{"Type":"test.connection","Name":"shared"}}},
         "Workflows":{
          "first":{"Resources":{"Connections":{"store":{"Type":"test.connection","Name":"local"}}},
            "local":{"Type":"test.use","Connection":"Connections.store"},
            "global":{"Type":"test.use","Connection":"Resources.Connections.store"}},
          "second":{"fallback":{"Type":"test.use","Connection":"Connections.store"},
            "cross":{"Type":"test.use","Connection":"first.Resources.Connections.store"}}
         }}
        """.Replace("first.Resources.Connections.store", failBuild ? "missing.Resources.Connections.store" : "first.Resources.Connections.store"));
        var log = new List<string>();
        var registrar = new Registrar(log);
        var descriptor = new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>("test.use", context =>
        {
            var connection = context.GetRequiredResource<Connection>("Connection");
            var node = new Use(connection, log);
            var registeredNode = node;
            context.HasInput("Input", node.Input);
            context.HasOutput("Output", node.Output);
            context.HasEvents(node.Events);
            return ValueTask.FromResult<IFlowNode>(registeredNode);
        });
        var services = new ServiceCollection();
        services.AddTestFactories([descriptor]);
        services.AddApplicationResourceRegistrar(registrar);
        await using var provider = services.BuildServiceProvider();
        if (failBuild)
            await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(provider));
        else
        {
            await using var app = await definition.BuildApplicationAsync(provider);
            await app.StartAsync();
            foreach (var (node, expected) in new[] { ("first.local", "local"), ("first.global", "shared"), ("second.fallback", "shared"), ("second.cross", "local") })
            {
                var result = await app.RequestAsync(node + ".Input", node + ".Output", FlowMessage.Create(JsonFlowValue.Null), TimeSpan.FromSeconds(5));
                result.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(expected);
            }
        }
        registrar.Created.ShouldBe(2);
        log.Count(entry => entry == "resource:local").ShouldBe(1);
        log.Count(entry => entry == "resource:shared").ShouldBe(1);
        log.FindLastIndex(entry => entry == "node").ShouldBeLessThan(log.FindIndex(entry => entry.StartsWith("resource:")));
    }

    [Fact]
    public void Csharp_workflow_resources_export_full_handles_and_nested_json()
    {
        var builder = new ApplicationDefinitionBuilder();
        var first = builder.AddWorkflow("first");
        var handle = first.AddResource<Connection>("Connections.store", "test.connection", resource => resource.Set("Name", "local"));
        handle.Address.Value.ShouldBe("first.Resources.Connections.store");
        Should.Throw<ArgumentException>(() => first.AddResource("Connections.store", "test.connection"));
        var definition = builder.Build().ToDefinition();
        definition.GetResourceValue("Connections.store.Name", "first").GetString().ShouldBe("local");
        ResourceInstanceDefinition.Enumerate(definition).Single().Key.ShouldBe(handle.Address);
        var copy = ApplicationDefinitionJson.Deserialize(ApplicationDefinitionJson.Serialize(definition));
        copy.GetResourceValue("first.Resources.Connections.store.Name").GetString().ShouldBe("local");
    }

    private sealed class Registrar(List<string> log) : IApplicationResourceRegistrar
    {
        public int Created;
        public void Register(ApplicationResourceRegistrationContext context)
        {
            foreach (var (address, resource) in ResourceInstanceDefinition.Enumerate(context.Definition))
                if (resource.Type == "test.connection")
                    context.Services.AddFluxFlowResource(address, _ =>
                    {
                        Created++;
                        return new Connection(resource.Properties["Name"].GetString()!, log);
                    });
        }
    }

    private sealed class Connection(string name, List<string> log) : IAsyncDisposable
    {
        public string Name => name;
        public ValueTask DisposeAsync() { log.Add("resource:" + name); return ValueTask.CompletedTask; }
    }

    private sealed class Use(Connection connection, List<string> log) : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => Task.FromResult(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(connection.Name, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        protected override ValueTask OnDisposeAsync() { log.Add("node"); return ValueTask.CompletedTask; }
    }
}
