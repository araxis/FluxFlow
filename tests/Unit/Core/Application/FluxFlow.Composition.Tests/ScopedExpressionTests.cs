using FluxFlow.Testing;
using FluxFlow.Json.Links;
using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ScopedExpressionTests
{
    [Fact]
    public void Parameter_projection_detaches_values_from_caller_owned_documents_and_dictionaries()
    {
        var parameters = new Dictionary<string, JsonElement>();
        ApplicationLinkDeclarationProjection projection;
        using (var document = JsonDocument.Parse("""{"value":7}"""))
        {
            parameters.Add("nested", document.RootElement);
            projection = new ApplicationLinkDeclarationProjection(
                ApplicationAddress.WorkflowPort("Main", "Source", "Output"),
                ApplicationAddress.WorkflowPort("Main", "Sink", "Input"), "true", parameters: parameters);
        }
        parameters.Clear();
        projection.Parameters.ShouldHaveSingleItem().Value.GetProperty("value").GetInt32().ShouldBe(7);
        ApplicationLinkCompiler.SerializeDeclarations([projection]).GetProperty("Parameters")
            .GetProperty("nested").GetProperty("value").GetInt32().ShouldBe(7);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Source_workflow_selects_engine_for_both_declaration_sides(bool inputSide)
    {
        var output = inputSide ? "" : """, "Output":{"Port":"B.Sink.Input","Condition":"check","Parameters":{"minimum":10}}""";
        var input = inputSide ? """, "Input":{"Port":"A.Source.Output","Condition":"check","Parameters":{"minimum":10}}""" : "";
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"ExpressionEngine":"default","Resources":{},"Workflows":{
              "A":{"ExpressionEngine":"source","Source":{"Type":"node"OUTPUT}},
              "B":{"ExpressionEngine":"target","Sink":{"Type":"node"INPUT}}
            }}
            """.Replace("OUTPUT", output).Replace("INPUT", input));
        var source = new RecordingEngine(0);
        var fallback = new RecordingEngine(100);
        var resolutions = new List<string>();
        var result = new LinkCompilerFixture(Catalog(), fallback, resolveConditionEngine: name =>
        {
            resolutions.Add(name);
            return name == "source" ? source : fallback;
        }).Compile(definition);

        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("A.Source.Output");
        link.Target.Value.ShouldBe("B.Sink.Input");
        link.IsMatch(Context(11)).ShouldBeTrue();
        link.IsMatch(Context(10)).ShouldBeFalse();
        resolutions.ShouldBe(["source"]);
        source.Types.ShouldHaveSingleItem()["input"].ShouldBe(typeof(int));
        fallback.Types.ShouldBeEmpty();
    }

    [Fact]
    public void Parameters_are_isolated_and_cached_by_configuration()
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"node","Output":[
                {"Port":"Low.Input","Condition":"check","Parameters":{"minimum":5}},
                {"Port":"High.Input","Condition":"check","Parameters":{"minimum":50}},
                {"Port":"Same.Input","Condition":"check","Parameters":{"minimum":5}}]},
              "Low":{"Type":"node"},"High":{"Type":"node"},"Same":{"Type":"node"}
            }}}
            """);
        var engine = new RecordingEngine(0);
        var result = new LinkCompilerFixture(Catalog(), engine).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        result.Links.Count.ShouldBe(3);
        engine.Types.Count.ShouldBe(2);
        var context = Context(10);
        result.Links.Single(static link => link.Target.Value == "Main.Low.Input").IsMatch(context).ShouldBeTrue();
        result.Links.Single(static link => link.Target.Value == "Main.High.Input").IsMatch(context).ShouldBeFalse();
        result.Links.Single(static link => link.Target.Value == "Main.Same.Input").IsMatch(Context(5)).ShouldBeFalse();
        context.Variables.ContainsKey("minimum").ShouldBeFalse();
        var projection = result.Declarations.Single(static link => link.Target.Value == "Main.High.Input");
        ApplicationLinkCompiler.SerializeDeclarations([projection]).GetProperty("Parameters")
            .GetProperty("minimum").GetInt32().ShouldBe(50);
    }

    [Theory]
    [InlineData("\"Parameters\":[]")]
    [InlineData("\"Parameters\":{\"input\":5}")]
    [InlineData("\"Parameters\":{\"bad-name\":5}")]
    public void Invalid_expression_configuration_is_rejected(string parameters)
    {
        var json = """{"Resources":{},"Workflows":{"Main":{"Source":{"Type":"node","Output":{"Port":"Sink.Input","Condition":"check",PARAMETERS}},"Sink":{"Type":"node"}}}}"""
            .Replace("PARAMETERS", parameters);
        var definition = ApplicationDefinitionJson.Deserialize(json);
        var result = new LinkCompilerFixture(Catalog(), new RecordingEngine(0)).Compile(definition);
        result.Diagnostics.ShouldContain(static diagnostic => diagnostic.Code == ApplicationLinkDiagnosticCode.InvalidLinkDeclaration);
        result.Links.ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_parameters_and_parameters_without_a_condition_are_rejected()
    {
        const string json = """
            {"Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"node","Output":{"Port":"Sink.Input","Parameters":{"minimum":5}}},
              "Sink":{"Type":"node"}
            }}}
            """;
        var result = new LinkCompilerFixture(Catalog(), new RecordingEngine(0)).Compile(ApplicationDefinitionJson.Deserialize(json));
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(ApplicationLinkDiagnosticCode.InvalidLinkDeclaration);
        result.Links.ShouldBeEmpty();
        Should.Throw<JsonException>(() => ApplicationDefinitionJson.Deserialize(json.Replace("\"minimum\":5", "\"minimum\":5,\"minimum\":6")))
            .Message.ShouldContain("duplicate");
    }

    [Fact]
    public void Missing_named_engine_does_not_fall_back_and_when_needs_no_engine()
    {
        var builder = new ApplicationDefinitionBuilder().UseExpressions("missing");
        var main = builder.AddWorkflow("Main");
        var source = main.AddComponent("Source", "node").Output<int>("Output");
        source.LinkTo(main.AddComponent("Sink", "node").Input<int>("Input"), "check");
        var result = new LinkCompilerFixture(Catalog(), new RecordingEngine(0)).Compile(builder.Build());
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(ApplicationLinkDiagnosticCode.MissingConditionEngine);
        result.Links.ShouldBeEmpty();
        var code = new ApplicationDefinitionBuilder().UseExpressions("missing");
        var flow = code.AddWorkflow("Main");
        flow.AddComponent("Source", "node").Output<int>("Output")
            .LinkTo(flow.AddComponent("Sink", "node").Input<int>("Input"), when: static value => value > 3);
        var compiled = new LinkCompilerFixture(Catalog()).Compile(code.Build());
        compiled.Diagnostics.ShouldBeEmpty();
        compiled.Links.ShouldHaveSingleItem().IsConditional.ShouldBeTrue();
    }

    [Fact]
    public void Expression_selection_round_trips_and_freezes()
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.UseExpressions("default").ShouldBeSameAs(builder);
        var main = builder.AddWorkflow("Main");
        main.UseExpressions("local").ShouldBeSameAs(main);
        Should.Throw<ArgumentException>(() => main.AddComponent("ExpressionEngine", "node"));
        var definition = builder.Build();
        var json = ApplicationDefinitionJson.Serialize(definition.ToDefinition());
        var parsed = ApplicationDefinitionJson.Deserialize(json);
        parsed.ExpressionEngine.ShouldBe("default");
        parsed.Workflows["Main"].ExpressionEngine.ShouldBe("local");
        parsed.Workflows["Main"].Components.ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => main.UseExpressions("later"));
        Should.Throw<InvalidOperationException>(() => builder.UseExpressions("later"));
        Should.Throw<JsonException>(() => ApplicationDefinitionJson.Deserialize("""{"Resources":{},"Workflows":{},"ExpressionEngine":null}"""));
    }

    private static IReadOnlyDictionary<string, ComponentInstance> Catalog() => LinkCompilerFixture.Surfaces([LinkCompilerFixture.Surface("node", inputs: [ComponentPorts.Metadata<int>("Input")], outputs: [ComponentPorts.Metadata<int>("Output")])]);

    private static FlowMapContext Context(int input) => new() { Variables = new Dictionary<string, object?> { ["input"] = input } };

    private sealed class RecordingEngine(int offset) : IFlowExpressionEngine
    {
        public string Name => "recording";
        public List<IReadOnlyDictionary<string, Type>> Types { get; } = [];
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public IFlowCompiledExpression<T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> types)
        {
            Types.Add(types);
            return (IFlowCompiledExpression<T>)(object)new Check(offset);
        }
        private sealed class Check(int offset) : IFlowCompiledExpression<bool>
        {
            public bool Evaluate(FlowMapContext context) => (int)context.Variables["input"]! > (int)context.Variables["minimum"]! + offset;
        }
    }
}
