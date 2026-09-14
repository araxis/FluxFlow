using FluxFlow.Json.Links;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class TypedCodeFirstApplicationDefinitionTests
{
    [Fact]
    public void Direct_connect_returns_same_output_and_records_ordered_local_and_cross_workflow_fanout()
    {
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = new OutputComponentHandle<Order>(main.AddComponent("Source", "test.source"), "Orders", "Events");
        var priority = new InputComponentHandle<Order>(main.AddComponent("Priority", "test.sink"), "Orders", "Events");
        var standard = new InputComponentHandle<Order>(main.AddComponent("Standard", "test.sink"), "Orders", "Events");
        var auditSink = new SignalComponentHandle(audit.AddComponent("Recorder", "test.audit"));

        var returned = source.Output
            .LinkTo(priority.Input)
            .LinkTo(standard.Input, "input.Priority == false")
            .LinkTo(auditSink.Signal, static order => order.Priority);

        returned.ShouldBeSameAs(source.Output);
        var definition = application.Build();
        definition.Links.Count.ShouldBe(3);
        definition.Links.Select(static link => link.Source.Value)
            .ShouldAllBe(static source => source == "Main.Source.Orders");
        definition.Links.Select(static link => link.Target.Value).ShouldBe(
        [
            "Main.Priority.Orders",
            "Main.Standard.Orders",
            "Audit.Recorder.Signal"
        ]);
        definition.Links[0].IsConditional.ShouldBeFalse();
        definition.Links[1].ConditionExpression.ShouldBe("input.Priority == false");
        definition.Links[2].ConditionExpression.ShouldBeNull();
        definition.Links[2].IsConditional.ShouldBeTrue();
        definition.Links.ShouldAllBe(static link =>
            link.MessageType == typeof(Order) &&
            link.DeclarationSide == ApplicationLinkDeclarationSide.Output);
    }

    [Fact]
    public void Connection_entry_points_share_scope_owner_duplicate_and_cardinality_rules()
    {
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = main.AddComponent("Source", "test.source");
        var local = main.AddComponent("Local", "test.sink");
        var remote = audit.AddComponent("Remote", "test.sink");
        var sourceOutput = source.Output<int>(
            "Value",
            ComponentPortLinkCardinality.Single);
        var localInput = local.Input<int>("Value");
        var remoteInput = remote.Input<int>("Value");

        var wrongScope = Should.Throw<InvalidOperationException>(() =>
            main.Connect(sourceOutput, remoteInput));
        wrongScope.Message.ShouldContain("Audit");
        application.Connect(sourceOutput, remoteInput).ShouldBeSameAs(application);

        var cardinality = Should.Throw<InvalidOperationException>(() =>
            sourceOutput.LinkTo(localInput));
        cardinality.Message.ShouldContain("Main.Source.Value");
        cardinality.Message.ShouldContain("only one connection");

        var foreignApplication = new ApplicationDefinitionBuilder();
        var foreign = foreignApplication.AddWorkflow("Foreign");
        var foreignInput = foreign.AddComponent("Sink", "test.sink").Input<int>("Value");
        var ownership = Should.Throw<InvalidOperationException>(() =>
            application.Connect(sourceOutput, foreignInput));
        ownership.Message.ShouldContain("different application definition builder");

        var definition = application.Build();
        definition.Links.ShouldHaveSingleItem();
        definition.Links[0].Target.ShouldBe(remoteInput.Address);
        Should.Throw<InvalidOperationException>(() =>
            application.Connect(sourceOutput, remoteInput));
    }

    [Fact]
    public void Null_conditions_fail_before_connection_mutation_and_preserve_parameter_names()
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.source");
        var sink = workflow.AddComponent("Sink", "test.sink");
        var output = source.Output<string?>("Value");
        var input = sink.Input<string?>("Value");

        var nullString = Should.Throw<ArgumentException>(() =>
            output.LinkTo(input, (string)null!));
        nullString.ParamName.ShouldBe("condition");
        var blankString = Should.Throw<ArgumentException>(() =>
            workflow.Connect(output, input, "  "));
        blankString.ParamName.ShouldBe("condition");
        var nullPredicate = Should.Throw<ArgumentNullException>(() =>
            application.Connect(output, input, (Func<string?, bool>)null!));
        nullPredicate.ParamName.ShouldBe("when");

        output.LinkTo(input).ShouldBeSameAs(output);
        var definition = application.Build();
        definition.Links.ShouldHaveSingleItem();
        definition.Links[0].IsConditional.ShouldBeFalse();
    }

    private sealed class SignalComponentHandle : AuthoredComponentHandle
    {
        public SignalComponentHandle(ComponentHandle definition)
            : base(definition)
        {
            Signal = definition.SignalInput("Signal");
            Events = definition.Output<global::FluxFlow.Data.FlowValue>("Events");
        }

        public SignalInputPortHandle Signal { get; }

        public OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; }
    }

    private sealed record Order(bool Priority = false);
}
