#:project ../../src/Studio/FluxFlow.Studio.UI/FluxFlow.Studio.UI.csproj
#:project ../../src/Hosting/FluxFlow.Execution/FluxFlow.Execution.csproj
#:property PublishAot=false
#:property PublishTrimmed=false
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Execution.Plugins;
using FluxFlow.Environments;
using FluxFlow.Environments.Telemetry;
using FluxFlow.Studio.UI.Features.Designer;

var directory = args[0];
using var plugins = InstalledPlugins.Load(Path.Combine(directory, "plugins"));
var descriptions = plugins.Modules.SelectMany(m => m.Description.Components);
var mode = args.ElementAtOrDefault(2);
if (mode is "removed" or "reinstalled")
{
    var pluginId = args[3];
    if (plugins.Modules.Any(module => module.Description.Id == pluginId) != (mode == "reinstalled"))
        throw new InvalidOperationException($"Unexpected installation state for '{pluginId}'.");
    var roundTrip = Path.Combine(directory, "edited.fluxflow");
    var stored = new DesignerGraphState(descriptions);
    stored.LoadProject(File.ReadAllText(roundTrip));
    if (stored.ExportConfiguration() != File.ReadAllText(Path.Combine(directory, "edited-export.json")))
        throw new InvalidOperationException($"Reopening with '{pluginId}' {mode} changed the configuration or wiring.");
    if (mode == "removed")
    {
        using var companion = new HttpClient { BaseAddress = new Uri(args[1]) };
        companion.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(stored.ExportConfiguration())), "configuration", "application.json");
        foreach (var action in new[] { "generate", "publish-compiled?targetMajor=10", "publish-compiled?targetMajor=11" })
        {
            using var response = await companion.PostAsync("/api/runs/" + action, payload);
            await CheckRejection(response, "Install the plugin for");
        }
    }
    File.WriteAllText(roundTrip, stored.ToProjectJson());
    return;
}
var compiled = mode is not null;
var catalog = mode == "catalog";
const string orderFile = "data/orders.json";
const string expressionPath = "data/total.jsonata";
const string expression = """
    {"LogLevel":"Information","MessageTemplate":"Order {Order} total {Total} cents","Args":[orderId,$sum(items.(quantity * unitCents))]}
    """;
var fixture = catalog
    ? JsonSerializer.Deserialize<Trial>(File.ReadAllText(Path.Combine(directory, "trial.json")), JsonSerializerOptions.Web)!
    : OrderTrial(mode);
var services = new ServiceCollection();
if (fixture.ExistingServices is { Count: > 0 } bindings)
    foreach (var owner in plugins.Modules.Where(m => m.Description.Connections.Any(c => bindings.Values.Any(b => b.Type == c.Type))))
        owner.ConfigureHost(services);
using var provider = services.BuildServiceProvider();
var graph = new DesignerGraphState(descriptions, provider.GetServices<IResourceEnvironmentAdapter>());
graph.LoadJson(fixture.Configuration.GetRawText());
if (fixture.ExistingServices is { Count: > 0 } existing)
{
    var topology = graph.GetEnvironmentDefinition().Topology;
    var host = new EnvironmentNode { Id = "local-host", Kind = EnvironmentNodeKind.Host, Name = "Local", LocalHost = true };
    var app = new EnvironmentNode { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Application", Host = host.Id };
    topology.Nodes.AddRange([host, app]);
    foreach (var (path, service) in existing)
    {
        var node = new EnvironmentNode { Id = "service-" + topology.Nodes.Count, Kind = EnvironmentNodeKind.Service,
            Name = service.Name, Host = host.Id, ExistingService = service };
        topology.Nodes.Add(node);
        app.Bindings.Add(path, node.Id);
    }
    graph.SetTopology(topology);
}
graph.ResetHistory();
var saved = graph.ToProjectJson();
var project = Path.Combine(directory, "trial.fluxflow");
File.WriteAllText(project, saved);
graph.ApplyEdits(graph.PrepareEdits(fixture.Edits.Select(edit =>
    new ConfigureComponentEdit(ComponentAddress.Parse(edit.Key), edit.Value)).ToArray()));
var edited = graph.ExportConfiguration();

var effective = await graph.ExportCompiledConfigurationAsync(graph.ActiveEnvironmentId);
var paths = PublishSourceFiles.Find(new DesignerConfiguration(effective), graph.Components, forRunner: false);
if (!paths.SequenceEqual(fixture.Files.Keys.Order(StringComparer.Ordinal)))
    throw new InvalidOperationException($"Unexpected selected files: {string.Join(", ", paths)}");
foreach (var path in paths)
{
    var file = Path.Combine(directory, path);
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, fixture.Files[path], new UTF8Encoding(true));
}
using var client = new HttpClient { BaseAddress = new Uri(args[1]), Timeout = TimeSpan.FromMinutes(5) };
client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
using var form = new MultipartFormDataContent();
form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(effective)), "configuration", "application.json");

foreach (var path in paths)
    form.Add(new ByteArrayContent(File.ReadAllBytes(Path.Combine(directory, path))), "source:" + Uri.EscapeDataString(path), "source");
if (fixture.SecretEnvironment is { } variables)
{
    using var generated = await client.PostAsync("/api/runs/generate", form);
    generated.EnsureSuccessStatusCode();
    var code = await generated.Content.ReadAsStringAsync();
    foreach (var (variable, value) in variables)
        if (!code.Contains($"Environment.GetEnvironmentVariable({JsonSerializer.Serialize(variable)})", StringComparison.Ordinal)
            || code.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException($"App.cs did not externalize the credential for {variable}.");
}
if (!catalog && compiled && args.Length == 3)
{
    using var generated = await client.PostAsync("/api/runs/generate", form);
    var code = await generated.Content.ReadAsStringAsync();
    if (!generated.IsSuccessStatusCode || !code.Contains(""".Every("tick", TimeSpan.FromTicks(1000000L), 1)""", StringComparison.Ordinal))
        throw new InvalidOperationException($"App.cs did not use the unsaved Scheduler capacity: {code}");
    var literal = JsonSerializer.Serialize(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(directory, orderFile))));
    if (!code.Contains($""".Source("orders", Convert.FromBase64String({literal}), 1)""", StringComparison.Ordinal))
        throw new InvalidOperationException("App.cs did not preserve the selected Source bytes and unsaved capacity.");
    if (!code.Contains($""".JsonataMapper("total", {JsonSerializer.Serialize(expression)}, false, 1)""", StringComparison.Ordinal))
        throw new InvalidOperationException("App.cs did not preserve the unsaved JSONata expression and settings.");
    if (!code.Contains(""".Logger("record", "Unsaved.Orders", 1)""", StringComparison.Ordinal))
        throw new InvalidOperationException("App.cs did not use the unsaved Logger category and capacity.");
}
var bothTargets = mode is "inline" or "file" or "catalog";
var endpoint = compiled ? "/api/runs/publish-compiled" : "/api/runs/generate";
if (UnregisteredEngine(mode))
{
    var uri = "/api/runs/" + Guid.NewGuid();
    using var start = await client.PostAsJsonAsync(uri, graph.CreateEnvironmentRunRequest(), EnvironmentJsonContext.Default.EnvironmentRunRequest);
    start.EnsureSuccessStatusCode();
    try
    {
        var status = await start.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.EnvironmentRunStatus);
        if (status?.State != "Failed" || status.Error?.Contains("unknown-engine", StringComparison.Ordinal) != true)
            throw new InvalidOperationException($"Unexpected Studio result for an unregistered Mapper engine: {status?.State}: {status?.Error}");
        Console.WriteLine("Studio Run rejected the unregistered Mapper engine.");
    }
    finally
    {
        using var stop = await client.DeleteAsync(uri);
        stop.EnsureSuccessStatusCode();
    }
}
if (bothTargets)
{
    for (var attempt = 1; attempt <= 2; attempt++)
    {
        var uri = "/api/runs/" + Guid.NewGuid();
        using var start = await client.PostAsJsonAsync(uri, graph.CreateEnvironmentRunRequest(), EnvironmentJsonContext.Default.EnvironmentRunRequest);
        start.EnsureSuccessStatusCode();
        var status = await start.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.EnvironmentRunStatus);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (fixture.StartupFailure is { } failure)
            {
                if (status?.State != "Failed" || status.Error?.Contains(failure, StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidOperationException($"Expected Studio startup failure containing '{failure}', got {status?.State}: {status?.Error}");
                File.WriteAllText(Path.Combine(directory, $"studio-{attempt}.json"), JsonSerializer.Serialize(failure));
                continue;
            }
            if (fixture.HttpCalls is { Length: > 0 } calls)
            {
                const string listening = "Now listening on: ";
                if (status?.State != "Running") throw new InvalidOperationException($"Studio Run failed: {status?.Error}");
                var logs = OtlpExecutionView.Logs(await client.GetByteArrayAsync(uri + "/telemetry/logs", timeout.Token), 0);
                var address = logs.Records.Single(r => r.Category == "Microsoft.Hosting.Lifetime" &&
                    r.Message.StartsWith(listening, StringComparison.Ordinal)).Message[listening.Length..];
                using var caller = new HttpClient { BaseAddress = new Uri(address) };
                foreach (var call in calls)
                {
                    var destination = call.Driver is null ? call.Path : call.Driver + call.Path;
                    using var content = new StringContent(call.Driver is null ? call.Body : address);
                    using var response = await caller.PostAsync(destination, content, timeout.Token);
                    if ((int)response.StatusCode != call.Status || await response.Content.ReadAsStringAsync(timeout.Token) != call.Response)
                        throw new InvalidOperationException($"Unexpected response for {call.Path}: {response.StatusCode}.");
                }
            }
            while (status?.State == "Running" && (fixture.RuntimeFailure is not null ||
                status.Outputs!.Records.Count(r => r.Port == fixture.LoggerOutput) < (fixture.StopAfter ?? fixture.Messages.Length)))
            {
                await Task.Delay(50, timeout.Token);
                status = await client.GetFromJsonAsync(uri, EnvironmentJsonContext.Default.EnvironmentRunStatus, timeout.Token);
            }
            if (fixture.RuntimeFailure is { } fault)
            {
                if (status?.State != "Failed" || status.Error?.Contains(fault, StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidOperationException($"Expected runtime failure '{fault}', got {status?.State}: {status?.Error}");
            }
            else if (status?.State != "Running") throw new InvalidOperationException($"Studio Run failed: {status?.Error}");
        }
        catch (OperationCanceledException error)
        {
            throw new TimeoutException($"Studio Run timed out waiting for {fixture.LoggerOutput}. Last observations: " +
                string.Join(", ", status?.Outputs?.Records.GroupBy(r => r.Port).Select(g => $"{g.Key}={g.Count()}") ?? []), error);
        }
        finally
        {
            using var stop = await client.DeleteAsync(uri);
            stop.EnsureSuccessStatusCode();
        }
        var bytes = await client.GetByteArrayAsync(uri + "/telemetry/logs");
        var records = OtlpExecutionView.Logs(bytes, 0).Records.Where(r => r.Category == fixture.Category)
            .Select(r => new[] { r.Level, r.Category, r.Message });
        File.WriteAllText(Path.Combine(directory, $"studio-{attempt}.json"), JsonSerializer.Serialize(records));
    }
    using var net10 = await client.PostAsync(endpoint + "?targetMajor=10", form);
    if (!net10.IsSuccessStatusCode) throw new InvalidOperationException(await net10.Content.ReadAsStringAsync());
    await File.WriteAllBytesAsync(Path.Combine(directory, "App-net10.exe"), await net10.Content.ReadAsByteArrayAsync());
    endpoint += "?targetMajor=11";
}
string[] endpoints = UnregisteredEngine(mode) && args.Length == 4
    ? [endpoint + "?targetMajor=10", endpoint + "?targetMajor=11"] : [endpoint];
foreach (var target in endpoints)
{
    using var response = await client.PostAsync(target, form);
    if (args.Length == 4)
    {
        await CheckRejection(response, args[3]);
        continue;
    }
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    var name = compiled ? "App.exe" : "App.cs";
    if (response.Content.Headers.ContentDisposition?.FileName?.Trim('"') != name)
        throw new InvalidOperationException("Unexpected download filename.");
    await File.WriteAllBytesAsync(Path.Combine(directory, name), await response.Content.ReadAsByteArrayAsync());
}
if (mode == "file")
{
    var inputs = Directory.CreateDirectory(Path.Combine(directory, "cli-inputs")).FullName;
    File.WriteAllText(Path.Combine(inputs, "application.json"), effective);
    foreach (var path in paths)
    {
        var copy = Path.Combine(inputs, path);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(Path.Combine(directory, path), copy);
    }
}
foreach (var path in paths) File.Delete(Path.Combine(directory, path));
graph.Undo();
if (graph.ToProjectJson() != saved) throw new InvalidOperationException("Generation changed the Undo checkpoint.");
graph.Redo();
if (graph.ExportConfiguration() != edited || File.ReadAllText(project) != saved)
    throw new InvalidOperationException("Generation changed the project or Redo checkpoint.");

var reopened = new DesignerGraphState(graph.Components);
reopened.LoadProject(graph.ToProjectJson());
if (reopened.ExportConfiguration() != edited)
    throw new InvalidOperationException("Reopening the project changed the edited workflow.");
if (bothTargets)
{
    var configuration = new DesignerConfiguration(edited);
    var types = configuration.WorkflowNames.SelectMany(configuration.Nodes).Select(n => n.Type).ToHashSet();
    var resources = configuration.Resources().Select(r => r.Type).ToHashSet();
    var owners = plugins.Modules.Where(m => m.Description.Components.Any(c => types.Contains(c.Palette.ComponentType)) ||
                                           m.Description.Connections.Any(c => resources.Contains(c.Type)))
        .Select(m => m.Description.Id).ToArray();
    File.WriteAllText(Path.Combine(directory, "edited.fluxflow"), reopened.ToProjectJson());
    File.WriteAllText(Path.Combine(directory, "edited-export.json"), edited);
    File.WriteAllText(Path.Combine(directory, "owners.json"), JsonSerializer.Serialize(owners));
}

static async Task CheckRejection(HttpResponseMessage response, string expected)
{
    var error = await response.Content.ReadAsStringAsync();
    if (response.StatusCode != System.Net.HttpStatusCode.BadRequest ||
        response.Content.Headers.ContentDisposition != null || !error.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected publish response ({response.StatusCode}): {error}");
    Console.WriteLine($"Publish rejected: {expected}. No download.");
}

static bool UnregisteredEngine(string? mode) => mode is "invalid-engine" or "invalid-file-engine";

static Trial OrderTrial(string? mode)
{
    var useFile = mode is "file" or "invalid-file-engine";
    var engine = UnregisteredEngine(mode) ? "unknown-engine" : "jsonata";
    var config = JsonSerializer.SerializeToElement(new { Workflows = new { main = new
    {
        tick = new { Type = "scheduler", Interval = "00:00:00.100", Output = "orders.Input" },
        orders = new { Type = "source", Values = "file:" + orderFile, Output = "total.Input" },
        total = new { Type = "mapper", Engine = engine, Expression = "$", Output = "record.Input" },
        record = new { Type = "logger", Category = "Saved.Orders" }
    } } });
    Dictionary<string, string> files = new() { [orderFile] = """
        [{"orderId":"B-23","items":[{"quantity":2,"unitCents":1250},{"quantity":3,"unitCents":150}]},
         {"orderId":"C-42","items":[{"quantity":4,"unitCents":275},{"quantity":1,"unitCents":99}]}]
        """ };
    if (useFile) files.Add(expressionPath, expression);
    var capacity = JsonSerializer.SerializeToElement(1);
    const string category = "Unsaved.Orders";
    return new(config, new()
    {
        ["main.total"] = new()
        {
            ["Expression"] = JsonSerializer.SerializeToElement(useFile ? "file:" + expressionPath : "inline:" + expression),
            ["HandleErrors"] = JsonSerializer.SerializeToElement(false), ["OutputCapacity"] = capacity
        },
        ["main.record"] = new() { ["Category"] = JsonSerializer.SerializeToElement(category), ["OutputCapacity"] = capacity },
        ["main.tick"] = new() { ["OutputCapacity"] = capacity },
        ["main.orders"] = new() { ["OutputCapacity"] = capacity }
    }, files, category, "main.record.Output", ["Order B-23 total 2950 cents", "Order C-42 total 1199 cents"]);
}

sealed record Trial(JsonElement Configuration, Dictionary<string, Dictionary<string, JsonElement?>> Edits,
    Dictionary<string, string> Files, string Category, string LoggerOutput, string[] Messages, HttpCall[]? HttpCalls = null, string? StartupFailure = null, string? RuntimeFailure = null,
    Dictionary<string, InfrastructureService>? ExistingServices = null, int? StopAfter = null, Dictionary<string, string>? SecretEnvironment = null);
sealed record HttpCall(string Path, string Body, int Status, string Response, string? Driver = null);
