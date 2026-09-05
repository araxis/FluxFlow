using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class TypedCodeFirstApplicationLinkCompilerTests
{
    [Fact]
    public void Compiler_compiles_typed_predicate_without_expression_engine_and_preserves_link_metadata()
    {
        var minimum = 5;
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = main.AddComponent("Source", "test.source");
        var sink = main.AddComponent("Sink", "test.sink");
        var signal = audit.AddComponent("Signal", "test.signal");
        source.Output<int>("Value")
            .ConnectTo(sink.Input<int>("Value"), value => value > minimum)
            .ConnectTo(signal.SignalInput("Trigger"), static value => value == 9);
        var definition = application.Build();

        var result = new ApplicationLinkCompiler(CreateCatalog()).Compile(definition);

        result.IsValid.ShouldBeTrue();
        result.Diagnostics.ShouldBeEmpty();
        result.Declarations.ShouldBeEmpty(
            "typed predicates must not be projected through portable link declarations.");
        result.Links.Count.ShouldBe(2);
        var local = result.Links.Single(static link =>
            link.Target.Value == "Main.Sink.Value");
        local.Source.Value.ShouldBe("Main.Source.Value");
        local.Target.Value.ShouldBe("Main.Sink.Value");
        local.MessageType.ShouldBe(typeof(int));
        local.ConditionExpression.ShouldBeNull();
        local.IsConditional.ShouldBeTrue();
        local.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);
        local.TryMatch(Context(6), out var localFailure).ShouldBeTrue();
        localFailure.ShouldBeNull();
        local.TryMatch(Context(4), out localFailure).ShouldBeFalse();
        localFailure.ShouldBeNull();
        minimum = 3;
        local.TryMatch(Context(4), out localFailure).ShouldBeTrue();
        localFailure.ShouldBeNull();

        var crossWorkflowSignal = result.Links.Single(static link =>
            link.Target.Value == "Audit.Signal.Trigger");
        crossWorkflowSignal.Source.Value.ShouldBe("Main.Source.Value");
        crossWorkflowSignal.MessageType.ShouldBe(typeof(int));
        crossWorkflowSignal.TryMatch(Context(9), out var signalFailure).ShouldBeTrue();
        signalFailure.ShouldBeNull();
        crossWorkflowSignal.TryMatch(Context(8), out signalFailure).ShouldBeFalse();
        signalFailure.ShouldBeNull();
    }

    [Fact]
    public void Typed_predicate_matcher_handles_false_null_error_and_exception_without_escape()
    {
        var nullableCalls = 0;
        var exception = new DistinctiveException("predicate failed");
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.nullable-source");
        var nullableSink = workflow.AddComponent("Nullable", "test.nullable-sink");
        var falseSink = workflow.AddComponent("False", "test.nullable-sink");
        var failingSink = workflow.AddComponent("Failing", "test.nullable-sink");
        source.Output<string?>("Value")
            .ConnectTo(
                nullableSink.Input<string?>("Value"),
                value =>
                {
                    nullableCalls++;
                    return value is null;
                })
            .ConnectTo(falseSink.Input<string?>("Value"), static _ => false)
            .ConnectTo(failingSink.Input<string?>("Value"), _ => throw exception);
        var result = new ApplicationLinkCompiler(CreateCatalog()).Compile(application.Build());

        result.IsValid.ShouldBeTrue();
        var nullable = result.Links.Single(static link =>
            link.Target.Value == "Main.Nullable.Value");
        nullable.TryMatch(Context<string?>(null), out var nullFailure).ShouldBeTrue();
        nullFailure.ShouldBeNull();
        nullableCalls.ShouldBe(1);

        nullable.TryMatch(ErrorContext<string?>(), out var errorFailure).ShouldBeFalse();
        errorFailure.ShouldBeNull();
        nullableCalls.ShouldBe(1);

        var falseLink = result.Links.Single(static link =>
            link.Target.Value == "Main.False.Value");
        falseLink.TryMatch(Context<string?>("value"), out var falseFailure).ShouldBeFalse();
        falseFailure.ShouldBeNull();

        var failing = result.Links.Single(static link =>
            link.Target.Value == "Main.Failing.Value");
        failing.TryMatch(Context<string?>("value"), out var caught).ShouldBeFalse();
        caught.ShouldBeSameAs(exception);
        nullable.TryMatch(Context<string?>(null), out var laterFailure).ShouldBeTrue();
        laterFailure.ShouldBeNull();
        nullableCalls.ShouldBe(2);
    }

    [Fact]
    public void Built_link_equality_reuses_same_definition_and_distinguishes_new_predicates()
    {
        Func<int, bool> predicate = static value => value > 0;
        var first = BuildSingleLink(predicate);
        var second = BuildSingleLink(predicate);
        var firstLink = first.Links.ShouldHaveSingleItem();
        var secondLink = second.Links.ShouldHaveSingleItem();

        firstLink.Equals(firstLink).ShouldBeTrue();
        firstLink.GetHashCode().ShouldBe(firstLink.GetHashCode());
        firstLink.Equals(secondLink).ShouldBeFalse(
            "each newly authored predicate link must own a new immutable revision identity.");

        var firstExpression = BuildSingleLink("value > 0").Links.ShouldHaveSingleItem();
        var secondExpression = BuildSingleLink("value > 0").Links.ShouldHaveSingleItem();
        firstExpression.Equals(secondExpression).ShouldBeTrue();
        firstExpression.GetHashCode().ShouldBe(secondExpression.GetHashCode());
    }

    private static ApplicationDefinition BuildSingleLink(Func<int, bool> when)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.source");
        var sink = workflow.AddComponent("Sink", "test.sink");
        source.Output<int>("Value").ConnectTo(sink.Input<int>("Value"), when);
        return application.Build();
    }

    private static ApplicationDefinition BuildSingleLink(string condition)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.source");
        var sink = workflow.AddComponent("Sink", "test.sink");
        source.Output<int>("Value").ConnectTo(sink.Input<int>("Value"), condition);
        return application.Build();
    }

    private static FlowMapContext Context<T>(T value)
        => new()
        {
            Variables = new Dictionary<string, object?>
            {
                ["message"] = FlowMessage.Create(value)
            }
        };

    private static FlowMapContext Context(int value) => Context<int>(value);

    private static FlowMapContext ErrorContext<T>()
        => new()
        {
            Variables = new Dictionary<string, object?>
            {
                ["message"] = FlowMessage.CreateError<T>(
                    new FlowError("test", "failed", "testing"))
            }
        };

    private static ComponentCatalog CreateCatalog()
        => new(
        [
            new ComponentDescriptor(
                "test.source",
                UnusedFactory,
                outputs: [ComponentPorts.Metadata<int>("Value")]),
            new ComponentDescriptor(
                "test.sink",
                UnusedFactory,
                inputs: [ComponentPorts.Metadata<int>("Value")]),
            new ComponentDescriptor(
                "test.signal",
                UnusedFactory,
                inputs: [ComponentPorts.SignalMetadata("Trigger")]),
            new ComponentDescriptor(
                "test.nullable-source",
                UnusedFactory,
                outputs: [ComponentPorts.Metadata<string?>("Value")]),
            new ComponentDescriptor(
                "test.nullable-sink",
                UnusedFactory,
                inputs: [ComponentPorts.Metadata<string?>("Value")])
        ]);

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Link compilation must not activate component factories.");

    private sealed class DistinctiveException(string message) : Exception(message);
}
