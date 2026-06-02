using FluxFlow.Engine;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using FluxFlow.SampleApp;
using System.Threading.Tasks.Dataflow;

var workspace = SampleWorkspaceDefinition.CreateDefault();
var store = new InMemoryOrderStore();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSampleOrderComponents(store);

await using var host = FlowApplicationHost.Create(
    workspace.ToEngineDefinition(),
    registry,
    new SampleExpressionEngine());

var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
{
    foreach (var error in build.Errors)
    {
        Console.Error.WriteLine(error.Message);
    }

    return 1;
}

var events = new BufferBlock<FlowEvent>(new DataflowBlockOptions { BoundedCapacity = 16 });
var diagnostics = new BufferBlock<RuntimeFlowDiagnostic>(new DataflowBlockOptions { BoundedCapacity = 16 });
host.Runtime.Events.LinkTo(events, new DataflowLinkOptions { PropagateCompletion = true });
host.Runtime.Diagnostics.LinkTo(diagnostics, new DataflowLinkOptions { PropagateCompletion = true });

var start = await host.StartBuiltAsync();
if (!start.IsSuccess)
{
    foreach (var error in start.Errors)
    {
        Console.Error.WriteLine(error.Message);
    }

    return 1;
}

await host.Runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
var observedEvents = await ReceiveManyAsync(events, expectedCount: 3, TimeSpan.FromSeconds(5));
var observedDiagnostics = await ReceiveManyAsync(diagnostics, expectedCount: 6, TimeSpan.FromSeconds(5));

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
Console.WriteLine($"Events observed: {observedEvents.Count}");
Console.WriteLine($"Diagnostics observed: {observedDiagnostics.Count}");

return 0;

static async Task<IReadOnlyList<T>> ReceiveManyAsync<T>(
    ISourceBlock<T> source,
    int expectedCount,
    TimeSpan timeout)
{
    var values = new List<T>();
    for (var index = 0; index < expectedCount; index++)
    {
        var value = await source.ReceiveAsync().WaitAsync(timeout);
        values.Add(value);
    }

    return values;
}
