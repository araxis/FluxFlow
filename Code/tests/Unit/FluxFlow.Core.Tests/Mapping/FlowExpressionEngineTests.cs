using FluxFlow.Core.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Mapping.Tests;

public sealed class FlowExpressionEngineTests
{
    [Fact]
    public void Default_compiled_expression_delegates_to_engine_evaluate()
    {
        var engine = new EvaluatingOnlyExpressionEngine();
        var compiled = ((IFlowExpressionEngine)engine).Compile<string>("value");

        var result = compiled(new FlowMapContext());

        result.ShouldBe("value:String");
        engine.EvaluateCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Default_compiler_rejects_missing_expression_before_evaluation(string? expression)
    {
        var engine = new EvaluatingOnlyExpressionEngine();
        Should.Throw<ArgumentException>(() => ((IFlowExpressionEngine)engine).Compile<string>(expression!));
        engine.EvaluateCalls.ShouldBe(0);
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
