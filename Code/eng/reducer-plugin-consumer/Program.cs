using System.Runtime.Loader;
using System.Text.Json;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Environments;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length != 1 || !Directory.Exists(args[0])) throw new ArgumentException("Supply an isolated plugin store.");
using var plugins = InstalledPlugins.Load(args[0]);
var module = plugins.Modules.Single(plugin => plugin.Description.Id == "FluxFlow.Plugins.Transformation");
if (AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) == AssemblyLoadContext.Default)
    throw new InvalidOperationException("Use the installed Transformation package, not a project reference.");
var services = new ServiceCollection().AddJsonataExpressionEngine();
module.ConfigureHost(services);
using var provider = services.BuildServiceProvider();
var fields = module.Description.Components.Single(c => c.Palette.ComponentType == "reducer").Inspector.Sections.SelectMany(s => s.Options);
var evaluator = new ExpressionEvaluator(provider);
foreach (var field in fields.Where(f => f.Name is "Reduce" or "AreEqual"))
{
    var sample = new ExpressionEvaluationRequest
    {
        Engine = "jsonata", PreviewProvider = field.PreviewProvider, ResultKind = field.ExpressionResult,
        PreviewSettings = new() { ["Engine"] = JsonSerializer.SerializeToElement("jsonata") },
        Input = field.PreviewSample!, Expression = field.Name == "Reduce" ? "$state + $input" : "$previous = $current"
    };
    var result = await evaluator.EvaluateAsync(sample);
    if (result.Error != null || result.Result!.Value.GetRawText() != (field.Name == "Reduce" ? "1" : "false"))
        throw new InvalidOperationException(result.Details ?? "Incorrect preview.");
    if ((await evaluator.EvaluateAsync(sample with { Input = "{}" })).Error == null)
        throw new InvalidOperationException("Missing sample fields were accepted.");
    if ((await evaluator.EvaluateAsync(sample with { Expression = "file:unopened.jsonata" })).Stage != "Source")
        throw new InvalidOperationException("Preview must not open files.");
}
using var logging = LoggerFactory.Create(_ => { });
var preparation = new EnvironmentPreparation([], []);
using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
    logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
var request = new EnvironmentRunRequest("""
    {"Workflows":{"main":{
      "tick":{"Type":"scheduler","Interval":"00:00:00.020","Output":"reduce.Input"},
      "reduce":{"Type":"reducer","Engine":"jsonata","InitialState":"0","Reduce":"$state + 1","AreEqual":"$previous = $current"}
    }}}
    """, new());
var original = JsonSerializer.Serialize(request);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
Guid previous = Guid.Empty;
string? retained = null;
for (var iteration = 0; iteration < 2; iteration++)
{
    var id = Guid.NewGuid();
    try
    {
        var status = await runner.StartAsync(id, request, deadline.Token);
        if (status.State != "Running") throw new InvalidOperationException(status.Error);
        while ((await runner.GetAsync(id, deadline.Token)).Outputs!.Records.Count(r => r.Port == "main.reduce.Output") < 3)
            await Task.Delay(10, deadline.Token);
        await runner.StopAsync(id, deadline.Token);
        status = await runner.GetAsync(id, deadline.Token);
        var values = status.Outputs!.Records.Where(r => r.Port == "main.reduce.Output").Select(r => r.Message!.Value.GetProperty("Value").GetInt32()).ToArray();
        if (status.State != "Stopped" || !values.SequenceEqual(Enumerable.Range(1, values.Length)))
            throw new InvalidOperationException(status.Error ?? "State or Stop failure.");
        if (retained != null && JsonSerializer.Serialize((await runner.GetAsync(previous, deadline.Token)).Outputs) != retained)
            throw new InvalidOperationException("Rerun changed retained results.");
        previous = id; retained = JsonSerializer.Serialize(status.Outputs);
    }
    finally { await runner.StopAsync(id, CancellationToken.None); }
}
if (JsonSerializer.Serialize(request) != original) throw new InvalidOperationException("Configuration changed.");
Console.WriteLine($"REDUCER_PLUGIN_TRIAL_PASSED {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
