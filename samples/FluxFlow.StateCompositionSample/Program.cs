using FluxFlow.Components.Mapping;
using FluxFlow.Components.Observability;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.State;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.Timers;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Engine;
using FluxFlow.Engine.Runtime;
using FluxFlow.StateCompositionSample;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

var definition = SampleApplicationDefinition.Create();
var capture = new SampleCapture();
var expressionEngine = new SampleExpressionEngine();
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterTimerComponents()
    .RegisterMappingComponents(options => options
        .UseExpressionEngine(expressionEngine)
        .RegisterType<TimerTick>("sample.timer.tick")
        .RegisterType<StateReducerInput>("sample.state.input")
        .UseContextFactory(new TimerTickContextFactory()))
    .RegisterStateComponents(options => options
        .UseExpressionEngine(expressionEngine))
    .RegisterObservabilityComponents(options => options
        .RegisterType<StateReducerResult>("sample.state.result"))
    .RegisterSampleComponents(capture);

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
    new DataflowBlockOptions { BoundedCapacity = 64 });
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
var observedDiagnostics = await ReceiveUntilCompletedAsync(
    diagnostics,
    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
var stateResults = capture.GetStateResults();
var counterSnapshots = capture.GetCounterSnapshots();
var finalState = stateResults.LastOrDefault();
var finalCount = ReadNumber(finalState?.NewState);
var finalCounter = counterSnapshots.LastOrDefault()?.Count ?? 0;

Console.WriteLine("Sample: state-composition");
Console.WriteLine($"State updates: {stateResults.Count}");
Console.WriteLine($"Counter snapshots: {counterSnapshots.Count}");
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Final state: key={finalState?.Key ?? "(none)"} value={finalCount} version={finalState?.Version ?? 0}"));
Console.WriteLine($"Final counter: {finalCounter}");
Console.WriteLine($"Diagnostics observed: {observedDiagnostics.Count}");

return finalCount == 3 && finalCounter == 3 ? 0 : 1;

static long ReadNumber(object? value)
    => value switch
    {
        null => 0,
        long number => number,
        int number => number,
        _ => throw new InvalidOperationException(
            $"Cannot read '{value.GetType().Name}' as a number.")
    };

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
