using FluxFlow.Components.Sessions;
using FluxFlow.Engine;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using FluxFlow.SessionsCompositionSample;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

var store = new InMemorySessionStore();
var capture = new SampleCapture();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSessionsComponents(options => options.UseStore(_ => store))
    .RegisterSampleComponents(capture);

var recordResult = await RunAsync(SampleApplicationDefinition.CreateRecording(), registry);
if (recordResult != 0)
{
    return recordResult;
}

var replayResult = await RunAsync(SampleApplicationDefinition.CreateReplay(), registry);
if (replayResult != 0)
{
    return replayResult;
}

var captured = capture.GetRecords();
var recorded = captured.Where(record => record.Stage == "recorded").ToArray();
var replayed = captured.Where(record => record.Stage == "replayed").ToArray();

Console.WriteLine("Sample: sessions-composition");
Console.WriteLine($"Recorded messages: {recorded.Length}");
Console.WriteLine($"Replayed messages: {replayed.Length}");
foreach (var record in replayed.OrderBy(record => record.Sequence))
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{record.Sequence}: {record.Name} payload={record.Payload}"));
}

return recorded.Length == replayed.Length ? 0 : 1;

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

    await host.Runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    await ReceiveUntilCompletedAsync(diagnostics, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
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
