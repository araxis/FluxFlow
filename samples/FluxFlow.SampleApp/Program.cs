using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Engine;
using FluxFlow.SampleApp;
using Microsoft.Extensions.DependencyInjection;

var workspace = SampleWorkspaceDefinition.CreateDefault();
var store = new InMemoryOrderStore();
var observedEvents = new InMemoryComponentEventCollector();

var services = new ServiceCollection();
services
    .AddSingleton(store)
    .AddSingleton(observedEvents)
    .AddFluxFlow(workspace.ToApplicationDefinition(), options => options.StartWithHost = false);

await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
var start = await application.StartAsync();
if (start.IsRejected)
{
    foreach (var diagnostic in start.Diagnostics)
    {
        Console.Error.WriteLine(
            $"{diagnostic.Error.Message} {JsonSerializer.Serialize(diagnostic.Error.Details)}");
    }
    return 1;
}

await WaitForResultsAsync(store, observedEvents, TimeSpan.FromSeconds(5));
await application.StopAsync();

Console.WriteLine($"Workspace: {workspace.Name}");
Console.WriteLine($"Views kept outside engine: {workspace.Views.Count}");
Console.WriteLine($"Checks kept outside engine: {workspace.Checks.Count}");
Console.WriteLine();

foreach (var stored in store.GetSnapshot())
{
    Console.WriteLine(
        $"{stored.Category}: {stored.Order.Id} {stored.Order.Customer} {stored.Order.Total:C} priority={stored.Order.Priority}");
}

Console.WriteLine();
Console.WriteLine($"Component events observed: {observedEvents.GetSnapshot().Count}");

return 0;

static async Task WaitForResultsAsync(
    InMemoryOrderStore store,
    InMemoryComponentEventCollector events,
    TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (store.GetSnapshot().Count < 3 || events.GetSnapshot().Count < 6)
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
}
