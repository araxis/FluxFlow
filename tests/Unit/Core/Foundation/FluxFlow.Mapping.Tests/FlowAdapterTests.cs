using Shouldly;
using Xunit;

namespace FluxFlow.Mapping.Tests;

public sealed class FlowAdapterTests
{
    [Fact]
    public void Delegate_mapper_uses_context_aware_delegate()
    {
        var mapper = new DelegateFlowMapper<string, string>(
            (input, context) => $"{input}:{context.Variables["suffix"]}");

        var result = mapper.Map(
            "value",
            new FlowMapContext
            {
                Variables = new Dictionary<string, object?>
                {
                    ["suffix"] = "mapped"
                }
            });

        result.ShouldBe("value:mapped");
    }

    [Fact]
    public void Delegate_mapper_and_predicate_reject_null_delegates()
    {
        Should.Throw<ArgumentNullException>(
                () => new DelegateFlowMapper<string, string>((Func<string, string>)null!))
            .ParamName.ShouldBe("map");
        Should.Throw<ArgumentNullException>(
                () => new DelegateFlowMapper<string, string>((Func<string, FlowMapContext, string>)null!))
            .ParamName.ShouldBe("map");
        Should.Throw<ArgumentNullException>(
                () => new DelegateFlowPredicate<string>(null!))
            .ParamName.ShouldBe("predicate");
    }

    [Fact]
    public void Delegate_predicate_evaluates_input()
    {
        var predicate = new DelegateFlowPredicate<string>(input => input.StartsWith("A", StringComparison.Ordinal));

        predicate.IsMatch("Alpha").ShouldBeTrue();
        predicate.IsMatch("Beta").ShouldBeFalse();
    }

    [Fact]
    public void Expression_mapper_compiles_once_and_evaluates_with_supplied_context()
    {
        var engine = new TestExpressionEngine
        {
            CompiledValue = context => $"{context.Variables["input"]}:{context.Variables["suffix"]}"
        };

        var mapper = new ExpressionFlowMapper<string, string>("input + suffix", engine);
        var result = mapper.Map(
            "ignored",
            new FlowMapContext
            {
                Variables = new Dictionary<string, object?>
                {
                    ["input"] = "value",
                    ["suffix"] = "mapped"
                }
            });

        result.ShouldBe("value:mapped");
        engine.CompileCalls.ShouldBe(1);
        engine.EvaluateCalls.ShouldBe(0);
        engine.CompiledEvaluateCalls.ShouldBe(1);
    }

    [Fact]
    public void Expression_predicate_uses_default_context_factory()
    {
        var engine = new TestExpressionEngine
        {
            CompiledValue = context =>
                (string?)context.Variables["input"] == "allowed"
                && (string?)context.Variables["value"] == "allowed"
        };

        var predicate = new ExpressionFlowPredicate<string>("input == value", engine);

        predicate.IsMatch("allowed").ShouldBeTrue();
        predicate.IsMatch("blocked").ShouldBeFalse();
        engine.CompileCalls.ShouldBe(1);
        engine.EvaluateCalls.ShouldBe(0);
        engine.CompiledEvaluateCalls.ShouldBe(2);
    }

    [Fact]
    public void Expression_predicate_uses_custom_context_factory()
    {
        var engine = new TestExpressionEngine
        {
            CompiledValue = context => (string?)context.Variables["custom"] == "allowed"
        };
        var predicate = new ExpressionFlowPredicate<string>(
            "custom == allowed",
            engine,
            new TestContextFactory());

        predicate.IsMatch("allowed").ShouldBeTrue();
        predicate.IsMatch("blocked").ShouldBeFalse();
    }

    [Fact]
    public void Expression_adapters_reject_invalid_constructor_arguments()
    {
        var engine = new TestExpressionEngine();

        Should.Throw<ArgumentException>(() => new ExpressionFlowMapper<string, string>(" ", engine))
            .ParamName.ShouldBe("expression");
        Should.Throw<ArgumentNullException>(() => new ExpressionFlowMapper<string, string>("value", null!))
            .ParamName.ShouldBe("engine");
        Should.Throw<ArgumentException>(() => new ExpressionFlowPredicate<string>(" ", engine))
            .ParamName.ShouldBe("expression");
        Should.Throw<ArgumentNullException>(() => new ExpressionFlowPredicate<string>("value", null!))
            .ParamName.ShouldBe("engine");
        Should.Throw<ArgumentNullException>(() => new ExpressionFlowPredicate<string>("value", engine, null!))
            .ParamName.ShouldBe("contextFactory");
    }

    [Fact]
    public void Expression_adapters_reject_null_compiled_expression_results()
    {
        var engine = new TestExpressionEngine
        {
            ReturnNullCompiledExpression = true
        };

        Should.Throw<InvalidOperationException>(() => new ExpressionFlowMapper<string, string>("value", engine))
            .Message.ShouldContain("returned null compiled expression");
        Should.Throw<InvalidOperationException>(() => new ExpressionFlowPredicate<string>("value", engine))
            .Message.ShouldContain("returned null compiled expression");
    }

    [Fact]
    public void Default_compiled_expression_delegates_to_engine_evaluate()
    {
        var engine = new EvaluatingOnlyExpressionEngine();
        var compiled = ((IFlowExpressionEngine)engine).Compile<string>("value");

        var result = compiled.Evaluate(new FlowMapContext());

        result.ShouldBe("value:String");
        engine.EvaluateCalls.ShouldBe(1);
    }

    private sealed class TestExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test";
        public int CompileCalls { get; private set; }
        public int EvaluateCalls { get; private set; }
        public int CompiledEvaluateCalls { get; set; }
        public bool ReturnNullCompiledExpression { get; init; }
        public Func<FlowMapContext, object?> CompiledValue { get; init; } = _ => default;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            EvaluateCalls++;
            return default;
        }

        public IFlowCompiledExpression<T> Compile<T>(string expression)
        {
            CompileCalls++;
            if (ReturnNullCompiledExpression)
                return null!;

            return new TestCompiledExpression<T>(this);
        }
    }

    private sealed class TestCompiledExpression<T>(TestExpressionEngine engine) : IFlowCompiledExpression<T>
    {
        public T Evaluate(FlowMapContext context)
        {
            engine.CompiledEvaluateCalls++;
            return (T)engine.CompiledValue(context)!;
        }
    }

    private sealed class TestContextFactory : IFlowMapContextFactory<string>
    {
        public FlowMapContext Create(string input)
            => new()
            {
                Variables = new Dictionary<string, object?>
                {
                    ["custom"] = input
                }
            };
    }

    private sealed class EvaluatingOnlyExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "evaluating";
        public int EvaluateCalls { get; private set; }

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            EvaluateCalls++;
            return $"{expression}:{resultType.Name}";
        }
    }
}
