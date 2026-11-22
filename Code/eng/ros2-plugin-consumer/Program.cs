using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2 || !Directory.Exists(args[0]) || !args[1].StartsWith("/fluxflow_test_", StringComparison.Ordinal))
    throw new ArgumentException("Supply an isolated plugin store and /fluxflow_test_<id> topic prefix.");
using var plugins = InstalledPlugins.Load(args[0]);
var module = plugins.Modules.Single(plugin => plugin.Description.Id == "FluxFlow.Plugins.Ros2");
if (AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) == AssemblyLoadContext.Default)
    throw new InvalidOperationException("The trial requires the installed package, not a project reference.");

var prefix = args[1];
for (var run = 0; run < 2; run++)
{
    var settings = """
        {"Resources":{"ros":{"Type":"ros.node","Name":"fluxflow_trial"}},"Workflows":{"main":{
          "receive":{"Type":"ros.subscriber","Node":"ros","Topic":"PREFIX/input","Output":"send.Input"},
          "send":{"Type":"ros.publisher","Node":"ros","Topic":"PREFIX/output"}}}}
        """.Replace("PREFIX", prefix);
    using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
    var services = new ServiceCollection();
    module.ConfigureWorkflow(services);
    services.AddFluxFlowApplication(configuration);
    await using var provider = services.BuildServiceProvider();
    await using var app = await provider.BuildApplicationAsync();
    var output = provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.send.Output");
    var capture = new BufferBlock<FlowMessage>();
    using var link = output.LinkTo(capture);
    await app.StartAsync();
    Console.WriteLine($"READY {run}");
    var instruction = await Console.In.ReadLineAsync();
    if (instruction != "stop") throw new InvalidOperationException("Expected stop after native delivery.");
    await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    if (!capture.TryReceiveAll(out var results) || results.Count == 0 || results.Any(message => message.IsError))
        throw new InvalidOperationException("Publication did not complete successfully.");
    var expected = new HashSet<string?> { "", $"run-{run}-λ\n\"hello\"", $"run-{run}-🌍" };
    if (!expected.SetEquals(results.Select(message => message.Value.Json.GetProperty("Data").GetString())))
        throw new InvalidOperationException("Payload changed or a previous run's message leaked into this run.");
    Console.WriteLine($"STOPPED {run}");
    if (await Console.In.ReadLineAsync() != "next") throw new InvalidOperationException("Expected next after observing shutdown.");
}
Console.WriteLine("ROS_PLUGIN_TRIAL_PASSED");
