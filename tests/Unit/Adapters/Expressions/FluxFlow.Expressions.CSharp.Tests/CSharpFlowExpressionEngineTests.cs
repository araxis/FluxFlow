using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Expressions.CSharp.Tests;

public sealed class CSharpFlowExpressionEngineTests
{
    [Fact]
    public void Json_parameter_values_retain_scalar_and_structured_types()
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"node","Output":{"Port":"Sink.Input",
                "Condition":"input > small && big == 2147483648L && fraction == 1.25m && huge == 1E40 && enabled && !disabled && empty == null && text == \"keep\" && list.GetArrayLength() == 2 && nested.GetProperty(\"value\").GetInt32() == 7",
                "Parameters":{"small":1,"big":2147483648,"fraction":1.25,"huge":1e40,
                  "enabled":true,"disabled":false,"empty":null,"text":"keep","list":[1,2],"nested":{"value":7}}}},
              "Sink":{"Type":"node"}
            }}}
            """);
        var catalog = new ComponentCatalog([new ComponentDescriptor("node",
            static _ => throw new InvalidOperationException("No activation."),
            inputs: [ComponentPorts.Metadata<int>("Input")], outputs: [ComponentPorts.Metadata<int>("Output")])]);
        var result = new ApplicationLinkCompiler(catalog, new CSharpFlowExpressionEngine()).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.IsMatch(Context(2)).ShouldBeTrue();
        link.IsMatch(Context(1)).ShouldBeFalse();
    }

    [Fact]
    public void Canonical_envelopes_and_errors_can_be_evaluated_without_unused_typed_inputs()
    {
        var catalog = new ComponentCatalog([new ComponentDescriptor("node",
            static _ => throw new InvalidOperationException("No activation."),
            inputs: [ComponentPorts.Metadata<global::FluxFlow.Data.FlowValue>("Input")],
            outputs: [ComponentPorts.Metadata<global::FluxFlow.Data.FlowValue>("Output")])]);
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"node","Output":{"Port":"Sink.Input",
                "Condition":"message.IsError || message.Value.ToJsonElement().GetProperty(\"count\").GetInt32() > minimum",
                "Parameters":{"minimum":10}}},"Sink":{"Type":"node"}
            }}}
            """);
        var result = new ApplicationLinkCompiler(catalog, new CSharpFlowExpressionEngine()).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        static FlowMapContext Context(FlowMessage message) => new()
        {
            Variables = new Dictionary<string, object?> { ["message"] = message },
        };
        link.IsMatch(Context(FlowMessage.Create(global::FluxFlow.Data.FlowValue.From(new { count = 11 })))).ShouldBeTrue();
        link.IsMatch(Context(FlowMessage.Create(global::FluxFlow.Data.FlowValue.From(new { count = 10 })))).ShouldBeFalse();
        link.IsMatch(Context(FlowMessage.CreateError(new global::FluxFlow.Data.FlowError("test", "failure", "test")))).ShouldBeTrue();
    }

    [Fact]
    public void Native_parser_options_are_applied()
    {
        var engine = new CSharpFlowExpressionEngine(options: DynamicExpresso.InterpreterOptions.DefaultCaseInsensitive,
            parameterTypes: new Dictionary<string, Type> { ["input"] = typeof(int) });
        engine.Compile<bool>("INPUT > 5").Evaluate(Context(6)).ShouldBeTrue();
        engine.Compile<bool>("INPUT > 5").Evaluate(Context(5)).ShouldBeFalse();
    }

    [Fact]
    public void Host_configuration_supports_functions_constants_and_referenced_types()
    {
        var configured = 0;
        IFlowExpressionEngine engine = new CSharpFlowExpressionEngine(interpreter =>
        {
            configured++;
            interpreter.SetVariable("offset", 2);
            interpreter.SetFunction("doubleIt", (Func<int, int>)(value => value * 2));
            interpreter.Reference(typeof(Rules));
        }, parameterTypes: new Dictionary<string, Type> { ["input"] = typeof(int) });

        var expression = engine.Compile<bool>("Rules.Positive(input) && doubleIt(input) > offset");
        expression.Evaluate(Context(2)).ShouldBeTrue();
        expression.Evaluate(Context(1)).ShouldBeFalse();
        expression.Evaluate(Context(-2)).ShouldBeFalse();
        engine.Evaluate<int>("doubleIt(input) + offset", Context(3)).ShouldBe(8);
        configured.ShouldBe(1);
        engine.Name.ShouldBe("csharp");
    }

    [Fact]
    public async Task Compiled_conditions_keep_concurrent_values_and_engine_instances_isolated()
    {
        static IFlowCompiledExpression<int> Create(int offset) => new CSharpFlowExpressionEngine(
            interpreter => interpreter.SetVariable("offset", offset))
            .Compile<int>("input + offset", new Dictionary<string, Type> { ["input"] = typeof(int) });
        var first = Create(10);
        var second = Create(100);

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            (First: first.Evaluate(Context(index)), Second: second.Evaluate(Context(index))))));

        results.Select(static pair => pair.First).ShouldBe(Enumerable.Range(10, 64));
        results.Select(static pair => pair.Second).ShouldBe(Enumerable.Range(100, 64));
    }

    [Fact]
    public void Compilation_rejects_invalid_conditions_and_assignments()
    {
        var engine = new CSharpFlowExpressionEngine(parameterTypes: new Dictionary<string, Type> { ["input"] = typeof(int) });
        Should.Throw<Exception>(() => engine.Compile<bool>("unknown > 0"));
        Should.Throw<Exception>(() => engine.Compile<bool>("input + 1"));
        Should.Throw<Exception>(() => engine.Compile<int>("input = 5"));
        Should.Throw<ArgumentException>(() => engine.Compile<bool>(" "));
        engine.Compile<bool>("input > 5").Evaluate(Context(6)).ShouldBeTrue();
    }

    [Fact]
    public void Json_conditions_use_typed_payloads_and_serializable_parameters()
    {
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"ExpressionEngine":"rules","Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"node","Output":{"Port":"Sink.Input",
                "Condition":"input > minimum && payload == input && message.Value == input",
                "Parameters":{"minimum":10}}},"Sink":{"Type":"node"}
            }}}
            """);
        var catalog = new ComponentCatalog([new ComponentDescriptor("node",
            static _ => throw new InvalidOperationException("Compilation must not activate components."),
            inputs: [ComponentPorts.Metadata<int>("Input")], outputs: [ComponentPorts.Metadata<int>("Output")])]);
        var engine = new CSharpFlowExpressionEngine();
        var result = new ApplicationLinkCompiler(catalog, resolveConditionEngine: name => name == "rules" ? engine : null)
            .Compile(definition);

        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.IsMatch(RuntimeContext(11)).ShouldBeTrue();
        link.IsMatch(RuntimeContext(10)).ShouldBeFalse();
    }

    private static FlowMapContext Context(int input) => new()
    {
        Variables = new Dictionary<string, object?> { ["input"] = input },
    };

    private static FlowMapContext RuntimeContext(int input) => new()
    {
        Variables = new Dictionary<string, object?>
        {
            ["input"] = input, ["payload"] = input, ["message"] = FlowMessage.Create(input),
        },
    };

    public static class Rules
    {
        public static bool Positive(int value) => value > 0;
    }
}
