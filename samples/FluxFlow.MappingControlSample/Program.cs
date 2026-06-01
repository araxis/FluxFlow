using FluxFlow.Components.Control;
using FluxFlow.Components.Mapping;
using FluxFlow.Engine;
using FluxFlow.Engine.Runtime;
using FluxFlow.MappingControlSample;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

var definition = SampleApplicationDefinition.Create();
var store = new SampleStore();
var expressionEngine = new SampleExpressionEngine();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMappingComponents(options => options
        .UseExpressionEngine(expressionEngine)
        .RegisterType<IncomingOrder>("sample.order.input")
        .RegisterType<ReviewedOrder>("sample.order.reviewed")
        .UseContextFactory(new IncomingOrderContextFactory()))
    .RegisterControlComponents(options => options
        .UseExpressionEngine(expressionEngine)
        .RegisterType<ReviewedOrder>("sample.order.reviewed")
        .UseContextFactory(new ReviewedOrderContextFactory()))
    .RegisterSampleComponents(store);

await using var host = FlowApplicationHost.Create(definition, registry);

var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
{
    foreach (var error in build.Errors)
    {
        Console.Error.WriteLine(error.Message);
    }

    return 1;
}

var diagnostics = new BufferBlock<RuntimeFlowDiagnostic>(
    new DataflowBlockOptions { BoundedCapacity = 32 });
host.Runtime.Diagnostics.LinkTo(
    diagnostics,
    new DataflowLinkOptions { PropagateCompletion = true });

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
var observedDiagnostics = await ReceiveUntilCompletedAsync(diagnostics, TimeSpan.FromSeconds(5));
var storedOrders = store.GetOrders()
    .OrderBy(stored => stored.Order.Id, StringComparer.Ordinal)
    .ToArray();
var assertions = store.GetAssertions();

Console.WriteLine("Sample: mapping-control");
Console.WriteLine($"Stored orders: {storedOrders.Length}");
foreach (var stored in storedOrders)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{stored.Category}: {stored.Order.Id} {stored.Order.Customer} total={stored.Order.Total:0.00} priority={stored.Order.Priority}"));
}

Console.WriteLine();
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Assertions: {assertions.Count(a => a.Passed)} passed, {assertions.Count(a => !a.Passed)} failed"));
Console.WriteLine($"Diagnostics observed: {observedDiagnostics.Count}");

return 0;

static async Task<IReadOnlyList<T>> ReceiveUntilCompletedAsync<T>(
    IReceivableSourceBlock<T> source,
    TimeSpan timeout)
{
    using var timeoutToken = new CancellationTokenSource(timeout);
    var values = new List<T>();
    try
    {
        while (await source.OutputAvailableAsync(timeoutToken.Token).ConfigureAwait(false))
        {
            while (source.TryReceive(out var value))
            {
                values.Add(value);
            }
        }
    }
    catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
    {
        throw new TimeoutException("Timed out while reading sample output.");
    }

    return values;
}
