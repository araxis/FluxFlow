using System.Text.Json;
using BenchmarkDotNet.Attributes;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Mapping;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
public class ExpressionBindingBenchmarks
{
    private IFlowCompiledExpression<bool> _expression = null!;
    private FlowMapContext _context = null!;

    [Params(8, 256)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var value = JsonSerializer.SerializeToElement(new
        {
            items = Enumerable.Range(0, ItemCount)
                .Select(index => new { value = index, label = $"item-{index}" }).ToArray()
        });
        _context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>
            {
                ["input"] = value,
                ["payload"] = value,
                ["minimum"] = -1
            }
        };
        _expression = new JsonataFlowExpressionEngine().Compile<bool>(
            "input.items[0].value = $input.items[0].value and payload.items[0].value > $minimum");
        if (!_expression.Evaluate(_context))
            throw new InvalidOperationException("The binding workload did not match its expected result.");
    }

    [Benchmark]
    public bool EvaluateCompiledCondition()
        => _expression.Evaluate(_context);
}
