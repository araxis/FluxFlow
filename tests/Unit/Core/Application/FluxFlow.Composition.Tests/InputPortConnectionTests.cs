using FluxFlow.Testing;
using FluxFlow.Json.Links;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class InputPortConnectionTests
{
    [Fact]
    public void Input_connections_return_target_and_preserve_declaration_side()
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var audit = application.AddWorkflow("Audit");
        var source = main.AddComponent("Source", "test.node").Output<int>("Output");
        var other = main.AddComponent("Other", "test.node").Output<int>("Output");
        var target = audit.AddComponent("Sink", "test.node").Input<int>("Input");
        var signal = main.AddComponent("Signal", "test.node").SignalInput("Trigger");

        target.LinkFrom(source).LinkFrom(other).ShouldBeSameAs(target);
        signal.LinkFrom(source).ShouldBeSameAs(signal);

        var definition = application.Build();
        definition.Links.Select(static link =>
            (link.Source.Value, link.Target.Value, link.DeclarationSide)).ShouldBe(
        [
            ("Main.Source.Output", "Audit.Sink.Input", ApplicationLinkDeclarationSide.Input),
            ("Main.Other.Output", "Audit.Sink.Input", ApplicationLinkDeclarationSide.Input),
            ("Main.Source.Output", "Main.Signal.Trigger", ApplicationLinkDeclarationSide.Input)
        ]);
        definition.Links.ShouldAllBe(static link => link.MessageType == typeof(int) && !link.IsConditional);
        var compiled = new LinkCompilerFixture(Catalog()).Compile(definition);
        compiled.Diagnostics.ShouldBeEmpty();
        compiled.Links.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flat_input_links_compile_like_json(bool branchAndMerge)
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var source = main.AddComponent("Source", "test.node");
        var first = main.AddComponent("First", "test.node");
        var sink = main.AddComponent("Sink", "test.node");
        first.Input<int>("Input").LinkFrom(source.Output<int>("Output"));
        sink.Input<int>("Input").LinkFrom(first.Output<int>("Output"));
        if (branchAndMerge)
        {
            var second = main.AddComponent("Second", "test.node");
            second.Input<int>("Input").LinkFrom(source.Output<int>("Output"));
            sink.Input<int>("Input").LinkFrom(second.Output<int>("Output"));
        }

        var json = branchAndMerge
            ? """
              {"Resources":{},"Workflows":{"Main":{
                "Source":{"Type":"test.node"},
                "First":{"Type":"test.node","Input":"Source.Output"},
                "Second":{"Type":"test.node","Input":"Source.Output"},
                "Sink":{"Type":"test.node","Input":["First.Output","Second.Output"]}
              }}}
              """
            : """
              {"Resources":{},"Workflows":{"Main":{
                "Source":{"Type":"test.node"},
                "First":{"Type":"test.node","Input":"Source.Output"},
                "Sink":{"Type":"test.node","Input":"First.Output"}
              }}}
              """;
        var compiler = new LinkCompilerFixture(Catalog());
        var authored = compiler.Compile(application.Build());
        var portable = compiler.Compile(ApplicationDefinitionJson.Deserialize(json));

        authored.Diagnostics.ShouldBeEmpty();
        portable.Diagnostics.ShouldBeEmpty();
        authored.Links.Count.ShouldBe(branchAndMerge ? 4 : 2);
        authored.Links.Select(Describe).ShouldBe(portable.Links.Select(Describe));
        authored.Links.ShouldContain(static link =>
            link.Source.Value == "Main.First.Output" && link.Target.Value == "Main.Sink.Input");
    }

    [Fact]
    public void Input_predicates_preserve_matching_and_signal_feedback()
    {
        var calls = 0;
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var source = main.AddComponent("Source", "test.node");
        var sink = main.AddComponent("Sink", "test.node");
        var input = sink.Input<int>("Input");
        var signal = source.SignalInput("Trigger");
        input.LinkFrom(source.Output<int>("Output"), value => { calls++; return value > 5; })
            .ShouldBeSameAs(input);
        signal.LinkFrom(sink.Output<int>("Output"), static value => value == 7)
            .ShouldBeSameAs(signal);

        var result = new LinkCompilerFixture(Catalog()).Compile(application.Build());
        result.Diagnostics.ShouldBeEmpty();
        result.Declarations.ShouldBeEmpty("C# predicates remain code-only.");
        result.Links.Count.ShouldBe(2);
        var data = result.Links.Single(static link => link.Target.Value == "Main.Sink.Input");
        data.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);
        data.IsConditional.ShouldBeTrue();
        data.IsMatch(Context(FlowMessage.Create(5))).ShouldBeFalse();
        data.IsMatch(Context(FlowMessage.Create(6))).ShouldBeTrue();
        data.IsMatch(Context(FlowMessage.CreateError<int>(new FlowError("test", "failed", "test"))))
            .ShouldBeFalse();
        calls.ShouldBe(2);
        var feedback = result.Links.Single(static link => link.Target.Value == "Main.Source.Trigger");
        feedback.IsMatch(Context(FlowMessage.Create(7))).ShouldBeTrue();
        feedback.IsMatch(Context(FlowMessage.Create(6))).ShouldBeFalse();
    }

    [Fact]
    public void Input_expression_links_retain_expression_and_input_declaration_metadata()
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var source = main.AddComponent("Source", "test.node").Output<int>("Output");
        var sink = main.AddComponent("Sink", "test.node");
        var input = sink.Input<int>("Input");
        var signal = sink.SignalInput("Trigger");

        input.LinkFrom(source, condition: "value > 5").ShouldBeSameAs(input);
        signal.LinkFrom(source, condition: "value == 7").ShouldBeSameAs(signal);

        var links = application.Build().Links;
        links.Select(static link => (link.Target.Value, link.ConditionExpression)).ShouldBe(
            [("Main.Sink.Input", "value > 5"), ("Main.Sink.Trigger", "value == 7")]);
        links.ShouldAllBe(static link => link.IsConditional &&
            link.DeclarationSide == ApplicationLinkDeclarationSide.Input && link.MessageType == typeof(int));
    }

    [Fact]
    public void Input_connections_reject_invalid_arguments_without_mutation()
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var source = main.AddComponent("Source", "test.node").Output<int>("Output");
        var sink = main.AddComponent("Sink", "test.node");
        var input = sink.Input<int>("Input");
        var signal = sink.SignalInput("Trigger");

        Should.Throw<ArgumentNullException>(() => input.LinkFrom(null!)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => input.LinkFrom(null!, "ok")).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => input.LinkFrom(null!, static _ => true)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => signal.LinkFrom<int>(null!)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => signal.LinkFrom<int>(null!, "ok")).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => signal.LinkFrom<int>(null!, static _ => true)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => ((InputPortHandle<int>)null!).LinkFrom(source)).ParamName.ShouldBe("target");
        Should.Throw<ArgumentNullException>(() => ((InputPortHandle<int>)null!).LinkFrom(source, "ok")).ParamName.ShouldBe("target");
        Should.Throw<ArgumentNullException>(() => ((InputPortHandle<int>)null!).LinkFrom(source, static _ => true)).ParamName.ShouldBe("target");
        Should.Throw<ArgumentNullException>(() => ((SignalInputPortHandle)null!).LinkFrom(source)).ParamName.ShouldBe("target");
        Should.Throw<ArgumentNullException>(() => ((SignalInputPortHandle)null!).LinkFrom(source, "ok")).ParamName.ShouldBe("target");
        Should.Throw<ArgumentNullException>(() => ((SignalInputPortHandle)null!).LinkFrom(source, static _ => true)).ParamName.ShouldBe("target");
        Should.Throw<ArgumentException>(() => input.LinkFrom(source, " "));
        Should.Throw<ArgumentException>(() => signal.LinkFrom(source, ""));
        Should.Throw<ArgumentNullException>(() => input.LinkFrom(source, when: null!));
        Should.Throw<ArgumentNullException>(() => signal.LinkFrom(source, when: null!));
        application.Build().Links.ShouldBeEmpty();
    }

    [Fact]
    public void Input_connections_reject_foreign_owners_and_frozen_builders()
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var source = main.AddComponent("Source", "test.node").Output<int>("Output");
        var input = main.AddComponent("Sink", "test.node").Input<int>("Input");
        var foreign = new ApplicationDefinitionBuilder();
        var foreignNode = foreign.AddWorkflow("Main").AddComponent("Foreign", "test.node");
        Should.Throw<InvalidOperationException>(() => input.LinkFrom(foreignNode.Output<int>("Output")));
        Should.Throw<InvalidOperationException>(() => foreignNode.SignalInput("Trigger").LinkFrom(source));

        input.LinkFrom(source);
        Should.Throw<InvalidOperationException>(() => input.LinkFrom(source));
        var definition = application.Build();
        Should.Throw<InvalidOperationException>(() => input.LinkFrom(source));
        Should.Throw<InvalidOperationException>(() => foreignNode.SignalInput("Trigger").LinkFrom(source));
        definition.Links.ShouldHaveSingleItem().Target.Value.ShouldBe("Main.Sink.Input");
        foreign.Build().Links.ShouldBeEmpty();
    }

    [Fact]
    public void Mixed_declaration_sides_do_not_bypass_single_link_cardinality()
    {
        var application = new ApplicationDefinitionBuilder();
        var main = application.AddWorkflow("Main");
        var first = main.AddComponent("First", "test.node").Output<int>("Output");
        var second = main.AddComponent("Second", "test.node").Output<int>("Output");
        var target = main.AddComponent("Sink", "test.node")
            .Input<int>("Input", ComponentPortLinkCardinality.Single);
        first.LinkTo(target);

        Should.Throw<InvalidOperationException>(() => target.LinkFrom(second));

        application.Build().Links.ShouldHaveSingleItem().Source.Value.ShouldBe("Main.First.Output");
    }

    private static (string, string, Type, string?, ApplicationLinkDeclarationSide) Describe(CompiledApplicationLink link)
        => (link.Source.Value, link.Target.Value, link.MessageType, link.ConditionExpression, link.DeclarationSide);

    private static FlowMapContext Context(FlowMessage<int> message)
        => new() { Variables = new Dictionary<string, object?> { ["message"] = message } };

    private static IReadOnlyDictionary<string, ComponentInstance> Catalog()
        => LinkCompilerFixture.Surfaces([LinkCompilerFixture.Surface("test.node", inputs: [ComponentPorts.Metadata<int>("Input"), ComponentPorts.SignalMetadata("Trigger")], outputs: [ComponentPorts.Metadata<int>("Output")])]);
}
