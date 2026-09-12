using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
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
            "\"DiscardUnmatched\":[\"Output\"],\"RepliesFrom\":[\"Output\"]" +
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
            properties["RepliesFrom"].EnumerateArray().Select(item => item.GetString()).ShouldBe(["Output"]);
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
        builder.RepliesFrom(output).ShouldBeSameAs(builder);
        var setup = builder.Build();
        var document = ApplicationDefinitionJson.Deserialize(FlowJson.Serialize(setup.ToDefinition()));
        var properties = document.Workflows["main"].Components["source"].Properties;
        properties["batch"].GetInt32().ShouldBe(7);
        foreach (var policy in new[] { "DiscardUnmatched", "RepliesFrom" })
            properties[policy].EnumerateArray().Select(item => item.GetString()).ShouldBe(["Output"]);
        foreach (var result in new[] { new ApplicationLinkCompiler(Catalog()).Compile(setup),
                     new ApplicationLinkCompiler(Catalog()).Compile(document) })
        {
            result.Diagnostics.ShouldBeEmpty();
            result.Links.ShouldBeEmpty();
            result.Declarations.ShouldBeEmpty();
            result.DiscardedOutputs.Select(address => address.Value).ShouldBe(["main.source.Output"]);
            result.ReplyOutputs.Select(address => address.Value).ShouldBe(["main.source.Output"]);
        }
        Should.Throw<InvalidOperationException>(() => output.DiscardUnmatched());
        Should.Throw<InvalidOperationException>(() => builder.RepliesFrom(output));
        var other = new ApplicationDefinitionBuilder();
        Should.Throw<InvalidOperationException>(() => other.RepliesFrom(output)).Message.ShouldContain("different application");
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
    [InlineData("discardUnmatched", "[\"Output\"]")]
    [InlineData("RepliesFrom", "{}")]
    [InlineData("RepliesFrom", "[\" Output\"]")]
    [InlineData("RepliesFrom", "[\"\"]")]
    [InlineData("RepliesFrom", "[\"Trigger\"]")]
    [InlineData("repliesFrom", "[\"Output\"]")]
    public async Task Invalid_output_policies_are_located_and_do_not_activate_components(string policy, string value)
    {
        var json = "{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"" + policy + "\":" + value + "}}}}";
        await AssertInvalidOutputPolicyAsync(json);
    }

    [Theory]
    [InlineData("DiscardUnmatched", false)]
    [InlineData("DiscardUnmatched", true)]
    [InlineData("RepliesFrom", false)]
    [InlineData("RepliesFrom", true)]
    public async Task Output_policy_case_aliases_are_rejected_regardless_of_property_order(string policy, bool aliasFirst)
    {
        var exact = $"\"{policy}\":[\"Output\"]";
        var alias = $"\"{policy.ToLowerInvariant()}\":[\"Output\"]";
        var properties = aliasFirst ? $"{alias},{exact}" : $"{exact},{alias}";
        await AssertInvalidOutputPolicyAsync("{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"," + properties + "}}}}");
    }

    [Theory]
    [InlineData("DiscardUnmatched")]
    [InlineData("RepliesFrom")]
    public void Empty_output_policy_lists_do_not_configure_any_output(string policy)
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.AddWorkflow("main").AddComponent("source", "source", options => options.Set(policy, Array.Empty<string>()));
        var definition = builder.Build().ToDefinition();
        definition.Workflows["main"].Components["source"].Properties[policy].GetArrayLength().ShouldBe(0);
        var result = new ApplicationLinkCompiler(Catalog()).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        result.DiscardedOutputs.ShouldBeEmpty();
        result.ReplyOutputs.ShouldBeEmpty();
        result.Links.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DiscardUnmatched", false)]
    [InlineData("DiscardUnmatched", true)]
    [InlineData("discardUnmatched", false)]
    [InlineData("discardUnmatched", true)]
    [InlineData("RepliesFrom", false)]
    [InlineData("RepliesFrom", true)]
    [InlineData("repliesFrom", false)]
    [InlineData("repliesFrom", true)]
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
        var result = new ApplicationLinkCompiler(Catalog()).Compile(definition);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldContain(error => error.Message.Contains("main.source.", StringComparison.Ordinal));
        result.DiscardedOutputs.ShouldBeEmpty();
        result.ReplyOutputs.ShouldBeEmpty();
        var source = new PassThrough();
        var bindings = new ComponentBindings();
        bindings.Add("source", _ => source);
        (await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(definition, bindings)))
            .Message.ShouldContain("main.source.");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        await using var services = new ServiceCollection().AddSingleton(Catalog()).BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(services)))
            .Message.ShouldContain("main.source.");
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
    public async Task Explicit_discard_and_reply_policies_execute_in_both_graphs(bool contract)
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
        builder.RepliesFrom(reply);
        var definition = builder.Build().ToDefinition();
        var bindings = Bindings(source, unmatched);
        bindings.Add("responder", _ => responder);
        var catalog = new ComponentCatalog([
            Descriptor("source", source), Descriptor("responder", responder),
            new ComponentDescriptor("target", _ => ValueTask.FromResult(new ComponentInstance(unmatched,
                inputs: [ComponentPorts.Input("Input", unmatched.Input)])), inputs: [ComponentPorts.Metadata<FlowValue>("Input")])]);
        await using var services = new ServiceCollection().AddSingleton(catalog)
            .AddSingleton<IFlowExpressionEngine>(new CSharpFlowExpressionEngine()).BuildServiceProvider();
        var application = contract ? await definition.BuildApplicationAsync(services)
            : await FlowJson.BuildAsync(FlowJson.Serialize(definition), bindings, new CSharpFlowExpressionEngine());
        try
        {
            await application.StartAsync();
            var original = FlowMessage.Create(FlowValue.From(7));
            (await application.SendAsync(application.GetInput("main.source.Input"), original)).ShouldBeTrue();
            var requests = Enumerable.Range(0, 3).Select(_ => application.RequestAsync(
                application.GetInput("main.responder.Input"), application.GetOutput("main.responder.Output"),
                original, Deadline)).ToArray();
            var replies = await Task.WhenAll(requests).WaitAsync(Deadline);
            replies.ShouldAllBe(message => message.TraceId == original.TraceId && message.MessageId == original.MessageId);
            replies.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
            replies.ShouldAllBe(message => message.Value!.Deserialize<int>() == 7);
            application.PendingRequestCount.ShouldBe(0);
            // Explicit reply intent absorbs late/duplicate reply traffic, not the fallback target.
            (await application.SendAsync(application.GetInput("main.responder.Input"), replies[0])).ShouldBeTrue();
            await application.StopAsync().WaitAsync(Deadline);
            application.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            source.Calls.ShouldBe(1);
            responder.Calls.ShouldBe(4);
            unmatched.Messages.ShouldBeEmpty();
        }
        finally { await application.DisposeAsync(); }
        source.Disposals.ShouldBe(1);
        responder.Disposals.ShouldBe(1);
        unmatched.Disposals.ShouldBe(1);

        static ComponentDescriptor Descriptor(string type, PassThrough component) => new(type,
            _ => ValueTask.FromResult(new ComponentInstance(component,
                inputs: [ComponentPorts.Input("Input", component.Input)],
                outputs: [ComponentPorts.Output("Output", component.Output)])),
            inputs: [ComponentPorts.Metadata<FlowValue>("Input")], outputs: [ComponentPorts.Metadata<FlowValue>("Output")]);
    }
}
