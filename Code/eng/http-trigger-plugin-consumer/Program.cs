using System.Net;
using System.Runtime.Loader;
using System.Text.Json;
using FluxFlow.Environments;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

if (args.Length != 1 || !Directory.Exists(args[0]))
    throw new ArgumentException("Supply an isolated plugin store containing HTTP and Transformation.");
using var plugins = InstalledPlugins.Load(args[0]);
foreach (var id in new[] { "FluxFlow.Plugins.Http", "FluxFlow.Plugins.Transformation" })
{
    var module = plugins.Modules.Single(plugin => plugin.Description.Id == id);
    if (AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) == AssemblyLoadContext.Default)
        throw new InvalidOperationException("The trial requires installed packages, not project references.");
}
using var logging = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
    options.AddProcessor(new ExecutionLogProcessor());
}));
var preparation = new EnvironmentPreparation([], []);
using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
    logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
var request = new EnvironmentRunRequest("""
    {"Workflows":{"main":{
      "incoming":{"Type":"http.trigger","Route":"/echo","ResponseMode":"Reply","Output":"reply.Input"},
      "reply":{"Type":"mapper","Engine":"jsonata","Expression":"{\"StatusCode\":200,\"Body\":Body}","Output":"incoming.Response"},
      "timeout":{"Type":"http.trigger","Route":"/timeout","ResponseMode":"Reply","ReplyTimeout":"00:00:00.100"},
      "pending":{"Type":"http.trigger","Route":"/pending","ResponseMode":"Reply"}
    }}}
    """, new());
var original = JsonSerializer.Serialize(request);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = deadline.Token;
var check = await runner.CheckAsync(request, token);
if (!check.Success) throw new InvalidOperationException(check.Message);
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
Guid previous = Guid.Empty;
string? retained = null;
for (var iteration = 0; iteration < 2; iteration++)
{
    var id = Guid.NewGuid();
    try
    {
        var status = await runner.StartAsync(id, request, token);
        if (status.State != "Running") throw new InvalidOperationException(status.Error);
        var url = status.Logs!.Records.Single(record => record.Category == "Microsoft.Hosting.Lifetime"
            && record.Message.StartsWith("Now listening on: ", StringComparison.Ordinal)).Message["Now listening on: ".Length..];
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async index =>
        {
            var payload = $"run-{iteration}/message-{index}/λ";
            using var response = await client.PostAsync(url + "/echo", new StringContent(payload), token);
            if (response.StatusCode != HttpStatusCode.OK || await response.Content.ReadAsStringAsync(token) != payload)
                throw new InvalidOperationException("The HTTP reply changed payload or correlation.");
        }));
        using var timeout = await client.PostAsync(url + "/timeout", new StringContent("timeout"), token);
        if (timeout.StatusCode != HttpStatusCode.GatewayTimeout) throw new InvalidOperationException("Expected HTTP 504.");
        var pending = client.PostAsync(url + "/pending", new StringContent("stop"), token);
        while ((await runner.GetAsync(id, token)).Outputs!.Records.All(record => record.Port != "main.pending.Output"))
            await Task.Delay(10, token);
        await runner.StopAsync(id, token);
        using var cancelled = await pending;
        if (cancelled.StatusCode != HttpStatusCode.ServiceUnavailable) throw new InvalidOperationException("Expected HTTP 503 on Stop.");
        try
        {
            using var unexpected = await client.PostAsync(url + "/echo", new StringContent("stopped"), token);
            throw new InvalidOperationException("The stopped listener still accepts connections.");
        }
        catch (HttpRequestException) { }
        status = await runner.GetAsync(id, token);
        if (status.State != "Stopped") throw new InvalidOperationException(status.Error);
        if (retained != null && JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs) != retained)
            throw new InvalidOperationException("Rerun changed retained observations.");
        retained = JsonSerializer.Serialize(status.Outputs);
        previous = id;
    }
    finally { await runner.StopAsync(id, CancellationToken.None); }
}
if (JsonSerializer.Serialize(request) != original) throw new InvalidOperationException("Run changed the saved configuration.");
Console.WriteLine($"HTTP_TRIGGER_PLUGIN_TRIAL_PASSED {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
