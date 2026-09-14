using FluxFlow.Testing;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed partial class PortableDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_document_options_exclude_output_policies(bool unknownOption)
    {
        var json = "{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Batch\":7," +
            "\"DiscardUnmatched\":[\"Output\"]" +
            (unknownOption ? ",\"Unexpected\":9" : "") + "}}}}";
        var definition = ApplicationDefinitionJson.Deserialize(json);
        var bindings = new ComponentBindings();
        PolicyOptions? bound = null;
        bindings.Add<PolicyOptions>("source", options => { bound = options; return new PassThrough(); },
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        if (unknownOption)
        {
            var error = await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(definition, bindings));
            error.Message.ShouldContain("Unexpected");
            bound.ShouldBeNull();
        }
        else
        {
            await using var application = await FlowJson.BuildAsync(definition, bindings);
            bound.ShouldNotBeNull().Batch.ShouldBe(7);
            var properties = definition.Workflows["main"].Components["source"].Properties;
            properties["DiscardUnmatched"].EnumerateArray().Select(item => item.GetString()).ShouldBe(["Output"]);
        }
    }

    private sealed record PolicyOptions(int Batch);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Portable_output_policies_round_trip_and_compile(bool configured)
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var output = workflow.AddComponent("source", "source", options =>
        {
            options.Set("batch", 7);
            if (configured) options.Set("DiscardUnmatched", new[] { "Output" });
        }).Output<FlowValue>("Output");
        output.DiscardUnmatched().ShouldBeSameAs(output);
        output.DiscardUnmatched().ShouldBeSameAs(output);
        var setup = builder.Build();
        var document = ApplicationDefinitionJson.Deserialize(FlowJson.Serialize(setup.ToDefinition()));
        var properties = document.Workflows["main"].Components["source"].Properties;
        properties["batch"].GetInt32().ShouldBe(7);
        foreach (var policy in new[] { "DiscardUnmatched" })
            properties[policy].EnumerateArray().Select(item => item.GetString()).ShouldBe(["Output"]);
        foreach (var result in new[] { new LinkCompilerFixture(Catalog()).Compile(setup),
                     new LinkCompilerFixture(Catalog()).Compile(document) })
        {
            result.Diagnostics.ShouldBeEmpty();
            result.Links.ShouldBeEmpty();
            result.Declarations.ShouldBeEmpty();
            result.DiscardedOutputs.Select(address => address.Value).ShouldBe(["main.source.Output"]);
        }
        Should.Throw<InvalidOperationException>(() => output.DiscardUnmatched());
    }

    [Theory]
    [InlineData("DiscardUnmatched", "null")]
    [InlineData("DiscardUnmatched", "true")]
    [InlineData("DiscardUnmatched", "\"Output\"")]
    [InlineData("DiscardUnmatched", "[null]")]
    [InlineData("DiscardUnmatched", "[\"Output\",\"Output\"]")]
    [InlineData("DiscardUnmatched", "[\"Input\"]")]
    [InlineData("DiscardUnmatched", "[\"missing\"]")]
    [InlineData("DiscardUnmatched", "[\"other.Output\"]")]
    [InlineData("discardUnmatched", "[\"missing\"]")]
    public async Task Invalid_output_policies_are_located_and_constructed_components_are_disposed(string policy, string value)
    {
        var json = "{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"" + policy + "\":" + value + "}}}}";
        await AssertInvalidOutputPolicyAsync(json);
    }

    [Theory]
    [InlineData("DiscardUnmatched", false)]
    [InlineData("DiscardUnmatched", true)]
    public async Task Output_policy_case_aliases_are_rejected_regardless_of_property_order(string policy, bool aliasFirst)
    {
        var exact = $"\"{policy}\":[\"Output\"]";
        var alias = $"\"{policy.ToLowerInvariant()}\":[\"Output\"]";
        var properties = aliasFirst ? $"{alias},{exact}" : $"{exact},{alias}";
        await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync("{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"," + properties + "}}}}", new ComponentBindings()));
    }

    [Theory]
    [InlineData("DiscardUnmatched")]
    public void Empty_output_policy_lists_do_not_configure_any_output(string policy)
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.AddWorkflow("main").AddComponent("source", "source", options => options.Set(policy, Array.Empty<string>()));
        var definition = builder.Build().ToDefinition();
        definition.Workflows["main"].Components["source"].Properties[policy].GetArrayLength().ShouldBe(0);
        var result = new LinkCompilerFixture(Catalog()).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        result.DiscardedOutputs.ShouldBeEmpty();
        result.Links.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DiscardUnmatched", false)]
    [InlineData("DiscardUnmatched", true)]
    [InlineData("discardUnmatched", false)]
    [InlineData("discardUnmatched", true)]
    public async Task Document_ports_cannot_shadow_output_policy_names(string name, bool input)
    {
        var component = new PolicyNamedComponent(name, input);
        var bindings = new ComponentBindings();
        bindings.Add("source", _ => component);
        var error = await Should.ThrowAsync<JsonException>(async () =>
        {
            await using var application = await FlowJson.BuildAsync(
                "{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"}}}}", bindings);
        });
        error.Message.ShouldContain($"main.source.{name}");
        error.Message.ShouldContain("reserved");
        component.Disposals.ShouldBe(1);
        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static async Task AssertInvalidOutputPolicyAsync(string json)
    {
        var definition = ApplicationDefinitionJson.Deserialize(json);
        var result = new LinkCompilerFixture(Catalog()).Compile(definition);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldContain(error => error.Message.Contains("main.source.", StringComparison.Ordinal));
        result.DiscardedOutputs.ShouldBeEmpty();
        var source = new PassThrough();
        var bindings = new ComponentBindings();
        bindings.Add("source", _ => source);
        (await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(definition, bindings)))
            .Message.ShouldContain("main.source.");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        var registered = new PassThrough();
        await using var services = new ServiceCollection().AddComponent("source", context =>
        {
            context.HasInput(registered.Input);
            context.HasOutput(registered.Output);
            return registered;
        }).BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(services)))
            .Message.ShouldContain("main.source.");
        registered.Calls.ShouldBe(0);
        registered.Disposals.ShouldBe(1);
    }

    private sealed class PolicyNamedComponent : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _block = new();
        public PolicyNamedComponent(string name, bool input)
            => Ports = input ? new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { [name] = _block })
                : new(outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { [name] = _block });
        public FlowPorts Ports { get; }
        public int Disposals { get; private set; }
        public Task Completion => _block.Completion;
        public void Complete() => _block.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_block).Fault(error);
        public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discard_policy_and_automatic_requests_execute_in_both_graphs(bool contract)
    {
        var source = new PassThrough();
        var responder = new PassThrough();
        var unmatched = new Sink();
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "source").Output<FlowValue>("Output");
        var reply = main.AddComponent("responder", "responder").Output<FlowValue>("Output");
        var target = main.AddComponent("target", "target").Input<FlowValue>("Input");
        output.LinkTo(target, condition: "false");
        output.DiscardUnmatched();
        reply.OtherwiseTo(target);
        var definition = builder.Build().ToDefinition();
        var bindings = Bindings(source, unmatched);
        bindings.Add("responder", _ => responder);
        KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>[] catalog = [
            Descriptor("source", source), Descriptor("responder", responder),
            new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>("target", context =>
        {
            var registeredNode = unmatched;
            context.HasInput("Input", unmatched.Input);
            return ValueTask.FromResult<IFlowNode>(registeredNode);
        })];
        await using var services = new ServiceCollection().AddTestFactories(catalog)
            .AddSingleton<IFlowExpressionEngine>(new CSharpFlowExpressionEngine()).BuildServiceProvider();
        var application = contract ? await definition.BuildApplicationAsync(services)
            : await FlowJson.BuildAsync(FlowJson.Serialize(definition), bindings, new CSharpFlowExpressionEngine());
        try
        {
            await application.StartAsync();
            var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(7, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await application.SendAsync(application.GetInput("main.source.Input"), original)).ShouldBeTrue();
            var requests = Enumerable.Range(0, 3).Select(_ => application.RequestAsync(
                application.GetInput("main.responder.Input"), application.GetOutput("main.responder.Output"),
                original, Deadline)).ToArray();
            var replies = await Task.WhenAll(requests).WaitAsync(Deadline);
            replies.ShouldAllBe(message => message.TraceId == original.TraceId && message.MessageId == original.MessageId);
            replies.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
            replies.ShouldAllBe(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 7);
            application.PendingRequestCount.ShouldBe(0);
            // Late replies no longer match a pending request and follow the configured fallback.
            (await application.SendAsync(application.GetInput("main.responder.Input"), replies[0])).ShouldBeTrue();
            await application.StopAsync().WaitAsync(Deadline);
            application.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            source.Calls.ShouldBe(1);
            responder.Calls.ShouldBe(4);
            unmatched.Messages.ShouldHaveSingleItem();
        }
        finally { await application.DisposeAsync(); }
        source.Disposals.ShouldBe(1);
        responder.Disposals.ShouldBe(1);
        unmatched.Disposals.ShouldBe(1);

        static KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> Descriptor(string type, PassThrough component)
            => new(type, context =>
            {
                context.HasInput(component.Input);
                context.HasOutput(component.Output);
                return ValueTask.FromResult<IFlowNode>(component);
            });
    }
}
