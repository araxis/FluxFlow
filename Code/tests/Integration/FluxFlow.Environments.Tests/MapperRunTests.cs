using System.Text.Json;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Execution;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class MapperRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_and_run_preserve_array_results_and_recover_after_a_bad_input(bool fromFile)
    {
        const string expression = "$input < 0 ? $error('negative reading') : [$input, $input * 2]";
        var directory = Directory.CreateTempSubdirectory("fluxflow-mapper-");
        var path = Path.Combine(directory.FullName, "transform reading.jsonata");
        var source = fromFile ? "file:" + path : "  InLiNe: " + expression;
        var request = new EnvironmentRunRequest(JsonSerializer.Serialize(new
        {
            Workflows = new { main = new
            {
                tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "samples.Input" },
                samples = new { Type = "source", Values = "[-1,21]", Output = "map.Input" },
                map = new { Type = "mapper", Engine = "jsonata", Expression = source }
            } }
        }), new());
        using var services = new ServiceCollection().AddJsonataExpressionEngine().BuildServiceProvider();
        var evaluator = new ExpressionEvaluator(services);
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var first = Guid.NewGuid();
        string? retained = null;
        try
        {
            if (fromFile)
            {
                var missing = Guid.NewGuid();
                try
                {
                    var failure = await runner.StartAsync(missing, request, token);
                    failure.State.ShouldBe("Failed");
                    failure.Error!.ShouldContain(path);
                }
                finally { await runner.StopAsync(missing, CancellationToken.None); }
                File.WriteAllText(path, expression);
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var sample = new ExpressionEvaluationRequest { Engine = "jsonata", Input = "21", Expression = source };
                if (fromFile)
                {
                    var deferred = await evaluator.EvaluateAsync(sample, token);
                    deferred.Stage.ShouldBe("Source");
                    deferred.Error!.ShouldContain("File evaluation is deferred");
                    sample = sample with { Expression = File.ReadAllText(path) };
                }
                var preview = await evaluator.EvaluateAsync(sample, token);
                preview.Error.ShouldBeNull();
                var expected = fromFile && attempt == 1 ? "[21,63]" : "[21,42]";
                preview.Result!.Value.GetRawText().ShouldBe(expected);
                var bad = await evaluator.EvaluateAsync(sample with { Input = "-1" }, token);
                bad.Stage.ShouldBe("Evaluate");
                bad.Error!.ShouldContain("negative reading");

                (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
                var id = attempt == 0 ? first : Guid.NewGuid();
                try
                {
                    (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                    if (fromFile && attempt == 0)
                        File.WriteAllText(path, expression.Replace("* 2", "* 3"));
                    while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.map.Output") < 2)
                        await Task.Delay(10, token);
                }
                finally { await runner.StopAsync(id, CancellationToken.None); }
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var output = stopped.Outputs!.Records.Where(r => r.Port == "main.map.Output").Select(r => r.Message!.Value).ToArray();
                output.Length.ShouldBe(2);
                output[0].GetProperty("IsError").GetBoolean().ShouldBeTrue();
                output[0].GetProperty("Error").GetRawText().ShouldContain("negative reading");
                output[1].GetProperty("Value").GetRawText().ShouldBe(preview.Result.Value.GetRawText());
                if (attempt == 0) retained = JsonSerializer.Serialize(stopped.Outputs);
                else JsonSerializer.Serialize((await runner.GetAsync(first, token)).Outputs).ShouldBe(retained);
            }
        }
        finally { File.Delete(path); directory.Delete(); }
    }
}
