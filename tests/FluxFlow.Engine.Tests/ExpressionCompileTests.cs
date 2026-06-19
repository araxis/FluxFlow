using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

/// <summary>
/// Verifies the build-time compile seam on IFlowExpressionEngine: predicates and
/// mappers compile an expression once and reuse the compiled form per message,
/// and engines that cannot pre-parse still work via the default Evaluate path.
/// </summary>
public sealed class ExpressionCompileTests
{
    [Fact]
    public void ExpressionFlowPredicate_CompilesOnce_AndReusesCompiledFormPerMessage()
    {
        var engine = new CountingExpressionEngine(result: true);
        var predicate = new ExpressionFlowPredicate<int>("input > 0", engine);

        engine.CompileCount.ShouldBe(1);   // compiled at construction (build time)
        engine.EvaluateCount.ShouldBe(0);

        for (var i = 0; i < 5; i++)
        {
            predicate.IsMatch(i).ShouldBeTrue();
        }

        engine.CompileCount.ShouldBe(1);    // never recompiled
        engine.CompiledEvaluateCount.ShouldBe(5);
        engine.EvaluateCount.ShouldBe(0);   // a precompiling engine bypasses Evaluate entirely
    }

    [Fact]
    public void ExpressionFlowMapper_CompilesOnce_AndReusesCompiledFormPerMessage()
    {
        var engine = new CountingExpressionEngine(result: 42);
        var mapper = new ExpressionFlowMapper<int, int>("input * 2", engine);

        engine.CompileCount.ShouldBe(1);

        for (var i = 0; i < 3; i++)
        {
            mapper.Map(i, new FlowMapContext()).ShouldBe(42);
        }

        engine.CompileCount.ShouldBe(1);
        engine.CompiledEvaluateCount.ShouldBe(3);
    }

    [Fact]
    public void DefaultCompile_DefersToEvaluate_ForEnginesThatDoNotPreParse()
    {
        // An engine that only implements Evaluate (no Compile override) still works:
        // the default compile wraps Evaluate, so the predicate evaluates per message.
        var engine = new EvaluateOnlyEngine(result: true);
        var predicate = new ExpressionFlowPredicate<int>("input > 0", engine);

        predicate.IsMatch(1).ShouldBeTrue();
        predicate.IsMatch(2).ShouldBeTrue();

        engine.EvaluateCount.ShouldBe(2);
    }

    private sealed class CountingExpressionEngine(object? result) : IFlowExpressionEngine
    {
        public int CompileCount { get; private set; }
        public int EvaluateCount { get; private set; }
        public int CompiledEvaluateCount { get; private set; }

        public string Name => "test.counting";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            EvaluateCount++;
            return result;
        }

        public IFlowCompiledExpression<T> Compile<T>(string expression)
        {
            CompileCount++;
            return new Compiled<T>(this, (T)result!);
        }

        private sealed class Compiled<T>(CountingExpressionEngine owner, T value) : IFlowCompiledExpression<T>
        {
            public T Evaluate(FlowMapContext context)
            {
                owner.CompiledEvaluateCount++;
                return value;
            }
        }
    }

    private sealed class EvaluateOnlyEngine(object? result) : IFlowExpressionEngine
    {
        public int EvaluateCount { get; private set; }

        public string Name => "test.evaluate-only";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            EvaluateCount++;
            return result;
        }
    }
}
