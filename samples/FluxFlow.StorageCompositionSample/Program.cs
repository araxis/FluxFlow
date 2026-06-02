using FluxFlow.Components.Storage;
using FluxFlow.Engine;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using FluxFlow.StorageCompositionSample;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

var store = new InMemoryStorageStore();
var capture = new SampleCapture();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterStorageComponents(options => options.UseSharedStore(store))
    .RegisterSampleComponents(capture);

var putResult = await RunAsync(SampleApplicationDefinition.CreatePut(), registry)
    .ConfigureAwait(false);
if (putResult != 0)
{
    return putResult;
}

var getResult = await RunAsync(SampleApplicationDefinition.CreateGet(), registry)
    .ConfigureAwait(false);
if (getResult != 0)
{
    return getResult;
}

var queryResult = await RunAsync(SampleApplicationDefinition.CreateQuery(), registry)
    .ConfigureAwait(false);
if (queryResult != 0)
{
    return queryResult;
}

var deleteResult = await RunAsync(SampleApplicationDefinition.CreateDelete(), registry)
    .ConfigureAwait(false);
if (deleteResult != 0)
{
    return deleteResult;
}

var captured = capture.GetResults();
var put = captured.Where(result => result.Stage == "put").ToArray();
var found = captured.Where(result => result.Stage == "get-found").ToArray();
var notFound = captured.Where(result => result.Stage == "get-not-found").ToArray();
var deleted = captured.Where(result => result.Stage == "delete").ToArray();
var queried = capture.GetQueryResults();

Console.WriteLine("Sample: storage-composition");
Console.WriteLine($"Put results: {put.Length}");
Console.WriteLine($"Get found: {found.Length}");
Console.WriteLine($"Get not found: {notFound.Length}");
Console.WriteLine($"Query results: {queried.Count}");
Console.WriteLine($"Delete results: {deleted.Length}");
Console.WriteLine($"Remaining records: {store.RecordCount}");
foreach (var result in captured)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Stage}: {result.Operation} {result.Key} found={result.Found} version={result.Version ?? 0} value={result.Value ?? "(none)"}"));
}

foreach (var result in queried)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Stage}: {result.Operation} {result.Collection} count={result.Count} keys={string.Join(",", result.Keys)}"));
}

return put.Length == 2 &&
       found.Length == 2 &&
       notFound.Length == 1 &&
       queried.Count == 1 &&
       queried[0].Count == 2 &&
       deleted.Length == 2 &&
       store.RecordCount == 1
    ? 0
    : 1;

static async Task<int> RunAsync(
    ApplicationDefinition definition,
    RuntimeNodeFactoryRegistry registry)
{
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

    var start = await host.StartBuiltAsync().ConfigureAwait(false);
    if (!start.IsSuccess)
    {
        foreach (var error in start.Errors)
        {
            Console.Error.WriteLine(error.Message);
        }

        return 1;
    }

    await host.Runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5))
        .ConfigureAwait(false);
    await ReceiveUntilCompletedAsync(diagnostics, TimeSpan.FromSeconds(5))
        .ConfigureAwait(false);
    return 0;
}

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
