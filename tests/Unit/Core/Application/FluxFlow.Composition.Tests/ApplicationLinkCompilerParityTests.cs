using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationLinkCompilerParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_endpoint_and_condition_keep_diagnostic_order_and_origin_projection(bool codeAuthored)
    {
        var engine = new RecordingExpressionEngine();
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "source", component =>
        {
            if (!codeAuthored)
                component.Set("Output", new { Port = "Sink.Missing", Condition = "invalid" });
        });
        var sink = workflow.AddComponent("Sink", "sink");
        if (codeAuthored)
            source.Output<string>("Output").ConnectTo(sink.Input<string>("Missing"), "invalid");

        var result = new ApplicationLinkCompiler(CreateCatalog(), engine).Compile(application.Build());

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
        [ApplicationLinkDiagnosticCode.MissingInputPort, ApplicationLinkDiagnosticCode.InvalidCondition]);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostic.WorkflowName.ShouldBe("Main");
            diagnostic.ComponentName.ShouldBe("Source");
            diagnostic.PropertyName.ShouldBe("Output");
            diagnostic.Source.ShouldBeNull();
        }
        result.Diagnostics[0].Message.ShouldBe("Component 'Main.Sink' has no input port 'Missing'.");
        result.Diagnostics[0].Target!.Value.ShouldBe("Main.Sink.Missing");
        result.Diagnostics[0].Exception.ShouldBeNull();
        result.Diagnostics[1].Target.ShouldBeNull();
        result.Diagnostics[1].Exception.ShouldBeSameAs(engine.Failure);
        result.Diagnostics[1].Message.ShouldBe(
            "Conditional link 'Main.Source.Output' has invalid expression 'invalid': Invalid expression.");
        engine.CompileCounts["invalid"].ShouldBe(1);
        if (codeAuthored)
        {
            result.Declarations.ShouldBeEmpty();
        }
        else
        {
            var declaration = result.Declarations.ShouldHaveSingleItem();
            declaration.DeclarationLocation.ShouldBe("Workflows.Main.Source.Output");
            declaration.PortReference.ShouldBe("Sink.Missing");
            declaration.ConditionExpression.ShouldBe("invalid");
        }
    }

    [Fact]
    public void Code_links_report_both_missing_endpoints_before_the_invalid_condition()
    {
        var engine = new RecordingExpressionEngine();
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "source");
        var sink = workflow.AddComponent("Sink", "sink");
        source.Output<string>("Missing").ConnectTo(sink.Input<string>("Missing"), "invalid");

        var result = new ApplicationLinkCompiler(CreateCatalog(), engine).Compile(application.Build());

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Declarations.ShouldBeEmpty();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
        [
            ApplicationLinkDiagnosticCode.MissingOutputPort,
            ApplicationLinkDiagnosticCode.MissingInputPort,
            ApplicationLinkDiagnosticCode.InvalidCondition
        ]);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostic.WorkflowName.ShouldBe("Main");
            diagnostic.ComponentName.ShouldBe("Source");
            diagnostic.PropertyName.ShouldBe("Missing");
        }
        result.Diagnostics[0].Source!.Value.ShouldBe("Main.Source.Missing");
        result.Diagnostics[0].Target.ShouldBeNull();
        result.Diagnostics[1].Source.ShouldBeNull();
        result.Diagnostics[1].Target!.Value.ShouldBe("Main.Sink.Missing");
        result.Diagnostics[2].Source.ShouldBeNull();
        result.Diagnostics[2].Target.ShouldBeNull();
        result.Diagnostics[2].Exception.ShouldBeSameAs(engine.Failure);
        engine.CompileCounts["invalid"].ShouldBe(1);
    }

    [Fact]
    public void Mixed_origins_share_condition_compilation_but_report_each_invalid_use()
    {
        var engine = new RecordingExpressionEngine();
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        workflow.AddComponent("Json", "source", component => component.Set("Output", new[]
        {
            new { Port = "Accepted.Input", Condition = "allow" },
            new { Port = "Rejected.Input", Condition = "invalid" }
        }));
        var code = workflow.AddComponent("Code", "source");
        var accepted = workflow.AddComponent("Accepted", "sink");
        var rejected = workflow.AddComponent("Rejected", "sink");
        code.Output<string>("Output")
            .ConnectTo(accepted.Input<string>("Input"), "allow")
            .ConnectTo(rejected.Input<string>("Input"), "invalid");

        var result = new ApplicationLinkCompiler(CreateCatalog(), engine).Compile(application.Build());

        engine.CompileCounts.Count.ShouldBe(2);
        engine.CompileCounts["allow"].ShouldBe(1);
        engine.CompileCounts["invalid"].ShouldBe(1);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.ComponentName))
            .ShouldBe(
            [
                (ApplicationLinkDiagnosticCode.InvalidCondition, "Json"),
                (ApplicationLinkDiagnosticCode.InvalidCondition, "Code")
            ]);
        foreach (var diagnostic in result.Diagnostics)
            diagnostic.Exception.ShouldBeSameAs(engine.Failure);
        result.Links.Select(link => (link.Source.Value, link.Target.Value)).ShouldBe(
        [("Main.Code.Output", "Main.Accepted.Input"), ("Main.Json.Output", "Main.Accepted.Input")]);
        foreach (var link in result.Links)
        {
            link.MessageType.ShouldBe(typeof(string));
            link.ConditionExpression.ShouldBe("allow");
            link.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);
            link.TryMatch(Context(true), out var failure).ShouldBeTrue();
            failure.ShouldBeNull();
            link.TryMatch(Context(false), out failure).ShouldBeFalse();
            failure.ShouldBeNull();
        }
        result.Declarations.Select(declaration => (declaration.DeclarationLocation, declaration.PortReference))
            .ShouldBe(
            [
                ("Workflows.Main.Json.Output", "Accepted.Input"),
                ("Workflows.Main.Json.Output", "Rejected.Input"),
                ("Workflows.Main.Code.Output", "Accepted.Input")
            ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exact_type_rejection_preserves_signal_sibling_for_both_origins(bool codeAuthored)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "source", component =>
        {
            if (!codeAuthored)
                component.Set("Output", new[] { "Object.Input", "Signal.Trigger" });
        });
        var objectSink = workflow.AddComponent("Object", "object-sink");
        var signal = workflow.AddComponent("Signal", "signal");
        if (codeAuthored)
        {
            source.Output<string>("Output")
                .ConnectTo(objectSink.Input<string>("Input"))
                .ConnectTo(signal.SignalInput("Trigger"));
        }

        var result = new ApplicationLinkCompiler(CreateCatalog()).Compile(application.Build());

        result.IsValid.ShouldBeFalse();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe(ApplicationLinkDiagnosticCode.PortTypeMismatch);
        diagnostic.Message.ShouldBe(
            "Link 'Main.Source.Output' to 'Main.Object.Input' connects 'System.String' to incompatible 'System.Object'.");
        diagnostic.Source!.Value.ShouldBe("Main.Source.Output");
        diagnostic.Target!.Value.ShouldBe("Main.Object.Input");
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("Main.Source.Output");
        link.Target.Value.ShouldBe("Main.Signal.Trigger");
        link.MessageType.ShouldBe(typeof(string));
        link.IsConditional.ShouldBeFalse();
        link.TryMatch(new FlowMapContext(), out var failure).ShouldBeTrue();
        failure.ShouldBeNull();
        result.Declarations.Count.ShouldBe(codeAuthored ? 1 : 2);
    }

    [Fact]
    public void Code_declared_type_is_checked_before_target_compatibility_and_after_condition_compilation()
    {
        var engine = new RecordingExpressionEngine();
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "source");
        var sink = workflow.AddComponent("Sink", "object-sink");
        source.Output<int>("Output").ConnectTo(sink.Input<int>("Input"), "allow");

        var result = new ApplicationLinkCompiler(CreateCatalog(), engine).Compile(application.Build());

        engine.CompileCounts["allow"].ShouldBe(1);
        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Declarations.ShouldBeEmpty();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe(ApplicationLinkDiagnosticCode.PortTypeMismatch);
        diagnostic.Message.ShouldBe(
            "Link 'Main.Source.Output' to 'Main.Sink.Input' declares 'System.Int32' but source port 'Main.Source.Output' carries 'System.String'.");
        diagnostic.Source!.Value.ShouldBe("Main.Source.Output");
        diagnostic.Target!.Value.ShouldBe("Main.Sink.Input");
    }

    [Fact]
    public void Input_declaration_diagnostics_retain_the_declaring_target_context()
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": { "Type": "source" },
                  "Sink": { "Type": "sink", "Input": { "Port": "Source.Missing", "Condition": "allow" } }
                }
              }
            }
            """);

        var result = new ApplicationLinkCompiler(CreateCatalog()).Compile(definition);

        result.Links.ShouldBeEmpty();
        result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
        [ApplicationLinkDiagnosticCode.MissingOutputPort, ApplicationLinkDiagnosticCode.MissingConditionEngine]);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostic.WorkflowName.ShouldBe("Main");
            diagnostic.ComponentName.ShouldBe("Sink");
            diagnostic.PropertyName.ShouldBe("Input");
            diagnostic.Target.ShouldBeNull();
        }
        result.Diagnostics[0].Source!.Value.ShouldBe("Main.Source.Missing");
        result.Diagnostics[1].Source.ShouldBeNull();
        result.Diagnostics[1].Message.ShouldBe(
            "Conditional link 'Main.Sink.Input' requires an expression engine.");
        var declaration = result.Declarations.ShouldHaveSingleItem();
        declaration.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);
        declaration.DeclarationLocation.ShouldBe("Workflows.Main.Sink.Input");
        declaration.PortReference.ShouldBe("Source.Missing");
    }

    private static FlowMapContext Context(bool allow)
        => new() { Variables = new Dictionary<string, object?> { ["allow"] = allow } };

    private static ComponentCatalog CreateCatalog()
        => new(
        [
            new ComponentDescriptor("source", UnusedFactory, outputs: [ComponentPorts.Metadata<string>("Output")]),
            new ComponentDescriptor("sink", UnusedFactory, inputs: [ComponentPorts.Metadata<string>("Input")]),
            new ComponentDescriptor("object-sink", UnusedFactory, inputs: [ComponentPorts.Metadata<object>("Input")]),
            new ComponentDescriptor("signal", UnusedFactory, inputs: [ComponentPorts.SignalMetadata("Trigger")])
        ]);

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Link compilation must not activate component factories.");

    private sealed class RecordingExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test";
        public FormatException Failure { get; } = new("Invalid expression.");
        public Dictionary<string, int> CompileCounts { get; } = new(StringComparer.Ordinal);

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Compiled expressions must be reused.");

        public IFlowCompiledExpression<T> Compile<T>(string expression)
        {
            CompileCounts[expression] = CompileCounts.GetValueOrDefault(expression) + 1;
            if (expression == "invalid")
                throw Failure;
            if (typeof(T) != typeof(bool))
                throw new NotSupportedException("Only Boolean conditions are supported.");
            return (IFlowCompiledExpression<T>)(object)new AllowExpression();
        }
    }

    private sealed class AllowExpression : IFlowCompiledExpression<bool>
    {
        public bool Evaluate(FlowMapContext context) => context.Variables["allow"] is true;
    }
}
