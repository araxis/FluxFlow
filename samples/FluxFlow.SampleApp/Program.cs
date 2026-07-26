using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Engine.Hosting;
using FluxFlow.Mapping;
using FluxFlow.SampleApp;
using Microsoft.Extensions.DependencyInjection;

var workspace = SampleWorkspaceDefinition.CreateDefault();
var store = new InMemoryOrderStore();
var observedEvents = new InMemoryComponentEventCollector();

var services = new ServiceCollection();
services.AddSingleton<IFlowExpressionEngine>(new SampleExpressionEngine());
services
    .AddFluxFlowApplication(workspace.ToApplicationDefinition())
    .AddFluxFlowEngine()
    .AddSampleOrderComponents(store, observedEvents);

await using var provider = services.BuildServiceProvider();
var host = provider.GetRequiredService<IApplicationRevisionHost>();
var start = await host.StartApplicationAsync();
if (!start.Succeeded)
{
    foreach (var failure in start.Update!.Failures)
    {
        Console.Error.WriteLine(
            $"{failure.Error.Message} {JsonSerializer.Serialize(failure.Error.Details)}");
    }
    return 1;
}

await WaitForResultsAsync(store, observedEvents, TimeSpan.FromSeconds(5));
await host.StopApplicationAsync();

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
