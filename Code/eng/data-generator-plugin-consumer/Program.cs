using System.Runtime.Loader;
using System.Text.Json;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Environments;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length != 1 || !Directory.Exists(args[0])) throw new ArgumentException("Supply an isolated Sources plugin store.");
using var plugins = InstalledPlugins.Load(args[0]);
var module = plugins.Modules.Single(plugin => plugin.Description.Id == "FluxFlow.Plugins.Sources");
if (AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) == AssemblyLoadContext.Default)
    throw new InvalidOperationException("Use the installed Sources package, not a project reference.");
var services = new ServiceCollection();
module.ConfigureHost(services);
using var provider = services.BuildServiceProvider();
var field = module.Description.Components.Single(component => component.Palette.ComponentType == "dataGenerator")
    .Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == "Expression");
var evaluator = new ExpressionEvaluator(provider);
var sample = new ExpressionEvaluationRequest
{
    Engine = field.Syntax, PreviewProvider = field.PreviewProvider,
    PreviewSettings = new() { ["Locale"] = JsonSerializer.SerializeToElement("sv") },
    Expression = "{'value':$fakeInt(7,7),'id':id,'name':$fakeFirstName()}", Input = "{\"id\":42}"
};
var preview = await evaluator.EvaluateAsync(sample);
if (preview.Error != null || preview.Result!.Value.GetProperty("value").GetInt32() != 7
    || preview.Result.Value.GetProperty("id").GetInt32() != 42)
    throw new InvalidOperationException(preview.Details ?? "Incorrect preview.");
if ((await evaluator.EvaluateAsync(sample with { Expression = "$fakeInt(9,1)" })).Stage != "Evaluate")
    throw new InvalidOperationException("Invalid arguments were not reported.");
if ((await evaluator.EvaluateAsync(sample with { Expression = "file:unopened.jsonata" })).Stage != "Source")
    throw new InvalidOperationException("Preview must not read files.");
if ((await evaluator.EvaluateAsync(sample)).Error != null) throw new InvalidOperationException("Preview did not recover.");

using var logging = LoggerFactory.Create(_ => { });
var preparation = new EnvironmentPreparation([], []);
using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
    logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
var request = new EnvironmentRunRequest("""
    {"Workflows":{"main":{
      "tick":{"Type":"scheduler","Interval":"00:00:00.030","Output":"source.Input"},
      "source":{"Type":"source","Values":"[{\"bad\":true},{\"bad\":false}]","Output":"generate.Input"},
      "generate":{"Type":"dataGenerator","Locale":"sv","Expression":"bad ? $fakeInt(9,1) : $fakeInt(7,7)"}
    }}}
    """, new());
var original = JsonSerializer.Serialize(request);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var check = await runner.CheckAsync(request, deadline.Token);
if (!check.Success) throw new InvalidOperationException(check.Message);
string? retained = null;
Guid previous = Guid.Empty;
for (var iteration = 0; iteration < 2; iteration++)
{
    var id = Guid.NewGuid();
    try
    {
        var status = await runner.StartAsync(id, request, deadline.Token);
        if (status.State != "Running") throw new InvalidOperationException(status.Error);
        while ((await runner.GetAsync(id, deadline.Token)).Outputs!.Records.Count(record => record.Port == "main.generate.Output") < 2)
            await Task.Delay(10, deadline.Token);
        await runner.StopAsync(id, deadline.Token);
        status = await runner.GetAsync(id, deadline.Token);
        var results = status.Outputs!.Records.Where(record => record.Port == "main.generate.Output").Select(record => record.Message!.Value).ToArray();
        if (status.State != "Stopped" || results.Length != 2 || !results[0].GetProperty("IsError").GetBoolean()
            || results[1].GetProperty("Value").GetInt32() != 7)
            throw new InvalidOperationException(status.Error ?? "Generation or Stop failed.");
        if (retained != null && JsonSerializer.Serialize((await runner.GetAsync(previous, deadline.Token)).Outputs) != retained)
            throw new InvalidOperationException("Rerun changed retained results.");
        retained = JsonSerializer.Serialize(status.Outputs);
        previous = id;
    }
    finally { await runner.StopAsync(id, CancellationToken.None); }
}
if (JsonSerializer.Serialize(request) != original) throw new InvalidOperationException("Run changed configuration.");
Console.WriteLine($"DATA_GENERATOR_PLUGIN_TRIAL_PASSED {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
