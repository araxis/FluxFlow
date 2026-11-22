using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

if (args.Length < 2) throw new ArgumentException("Usage: normal|windows-adapter|windows-service evidence-path [service-name]");
var mode = args[0];
var evidencePath = Path.GetFullPath(args[1]);
try { await VerifyAsync(); }
catch (Exception error)
{
    // Verification failures are separate from the host's unchanged logging pipeline.
    await PublishEvidenceAsync(evidencePath + ".failure.txt", error.ToString());
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

async Task VerifyAsync()
{
    if (mode is not ("normal" or "windows-adapter" or "windows-service")) throw new ArgumentException("Unknown mode.");
    var actualService = WindowsServiceHelpers.IsWindowsService();
    Check(actualService == (mode == "windows-service"), "SCM mode must actually run as a Windows Service; console fallback is not service verification.");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
    if (mode != "normal") builder.Services.AddWindowsService(options => options.ServiceName = args.Length > 2 ? args[2] : "FluxFlow.HostSmoke");
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
    var sourceAddress = ComponentAddress.Parse("main.source");
    var sinkAddress = ComponentAddress.Parse("main.sink");
    builder.Services.AddComponentFactory(sourceAddress, _ => new Source())
        .AddOutputPort<Source>(sourceAddress, "Output", node => node.Output)
        .AddEventPort<Source>(sourceAddress, node => node.Events);
    builder.Services.AddComponentFactory(sinkAddress, _ => new Sink())
        .AddInputPort<Sink>(sinkAddress, "Input", node => node.Input);
    builder.Services.AddSingleton<LinkFactory>(services => services.Link(sourceAddress.Port("Output"), sinkAddress.Port("Input")));
    builder.Services.AddFluxFlow();
    var host = builder.Build();
    Source? source = null;
    Sink? sink = null;
    string? lifetimeType = null;
    long dropped = 0;
    try
    {
        await host.StartAsync(deadline.Token);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Check(lifetime.ApplicationStarted.IsCancellationRequested, "Host did not signal started.");
        lifetimeType = host.Services.GetRequiredService<IHostLifetime>().GetType().Name;
        Check(lifetimeType == (actualService ? "WindowsServiceLifetime" : "ConsoleLifetime"), "Unexpected host lifetime.");
        source = (Source)host.Services.GetWorkflowServices(sourceAddress.WorkflowName).GetRequiredKeyedService<IFlowNode>(sourceAddress.Value);
        sink = (Sink)host.Services.GetWorkflowServices(sinkAddress.WorkflowName).GetRequiredKeyedService<IFlowNode>(sinkAddress.Value);
        using var stopping = lifetime.ApplicationStopping.Register(() => sink.Release.TrySetResult());
        Check((await sink.Received.ReceiveAsync(deadline.Token)).Value.Json.GetInt32() == 0, "Source started before business links were ready.");
        using var events = host.Services.ObserveOutput(sourceAddress.Port("Events"));
        var observedEvent = await events.Messages.ReceiveAsync(deadline.Token);
        Check(observedEvent.Headers[FlowEventHeaders.Source].Json.GetString() == sourceAddress.Value, "Event address was lost.");
        using var first = host.Services.ObserveOutput(sourceAddress.Port("Output"));
        await first.Messages.ReceiveAsync(deadline.Token); // Native retained-value replay.
        var message = await source.PublishAsync(1);
        Check(ReferenceEquals(await first.Messages.ReceiveAsync(deadline.Token), message), "Probe changed the message.");
        Check(ReferenceEquals(await sink.Received.ReceiveAsync(deadline.Token), message), "Probe interfered with delivery.");
        first.Dispose();
        await first.Messages.Completion.WaitAsync(deadline.Token);
        await source.PublishAsync(2);
        await sink.Received.ReceiveAsync(deadline.Token);
        using var next = host.Services.ObserveOutput(sourceAddress.Port("Output"));
        Check(next.Address.ComponentAddress == sourceAddress, "Probe lost component identity.");
        Check((await next.Messages.ReceiveAsync(deadline.Token)).Value.Json.GetInt32() == 2, "Reattached probe did not observe retained output.");
        using var slow = host.Services.ObserveOutput(sourceAddress.Port("Output"), capacity: 1);
        await slow.Messages.OutputAvailableAsync(deadline.Token);
        await source.PublishAsync(3);
        await sink.Received.ReceiveAsync(deadline.Token);
        dropped = slow.DroppedCount;
        Check(dropped > 0, "Expected a bounded slow probe to drop samples.");
        await source.PublishAsync(4);
        await sink.Entered.Task.WaitAsync(deadline.Token);
        Check(!sink.Completion.IsCompleted, "Expected accepted work pending at shutdown.");
        // Service mode waits for the driver's real SCM Stop command.
        await PublishEvidenceAsync(evidencePath + ".ready", lifetimeType!, deadline.Token);
        if (!actualService) lifetime.StopApplication();
        await host.WaitForShutdownAsync().WaitAsync(deadline.Token);
        Check(lifetime.ApplicationStopped.IsCancellationRequested, "Host did not signal stopped.");
        Check(host.Services.GetRequiredService<FlowApplication>().Completion.IsCompletedSuccessfully, "Application did not drain.");
        Check(source.Starts == 1 && sink.Values.SequenceEqual(Enumerable.Range(0, 5)), "Startup or shutdown lost business work.");
        Check(source.Disposals == 1 && sink.Disposals == 1, "Workflow scope did not dispose components at shutdown.");
    }
    finally
    {
        sink?.Release.TrySetResult();
        try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { await ((IAsyncDisposable)host).DisposeAsync(); }
    }
    Check(source?.Disposals == 1 && sink?.Disposals == 1, "DI did not dispose each component exactly once.");
    await PublishEvidenceAsync(evidencePath, JsonSerializer.Serialize(new
    {
        Mode = mode, ActualWindowsService = actualService, Lifetime = lifetimeType,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Passed = true, BusinessMessages = sink!.Values.ToArray(), DroppedProbeSamples = dropped,
        SourceDisposals = source!.Disposals, SinkDisposals = sink.Disposals
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"HOST_VERIFIED={mode}; Lifetime={lifetimeType}; Evidence={evidencePath}");
}

static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

static async Task PublishEvidenceAsync(string path, string content, CancellationToken cancellationToken = default)
{
    // The external driver must never read a partially written result or failure.
    var temporaryPath = path + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
    File.Move(temporaryPath, path);
}

sealed class Source : FlowNode, IFlowSource
{
    public int Starts { get; private set; }
    public int Disposals { get; private set; }
    public Source()
    {
        Output = new BroadcastBlock<FlowMessage>(static message => message);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, produce: ProduceAsync);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }
    public Task StartAsync(CancellationToken token = default) => Lifetime.StartAsync(token);
    public void StopProduction() => Lifetime.StopProduction();
    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    private async Task ProduceAsync(CancellationToken token)
    {
        Starts++;
        await PublishAsync(0);
        await Task.Delay(Timeout.Infinite, token);
    }
    public async Task<FlowMessage> PublishAsync(int value)
    {
        var message = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
        Events.Post(message);
        await EmitAsync(message);
        return message;
    }
    protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
}

sealed class Sink : IFlowNode
{
    private readonly ActionBlock<FlowMessage> _input;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public BufferBlock<FlowMessage> Received { get; } = new();
    public List<int> Values { get; } = [];
    public int Disposals { get; private set; }
    public Sink() => _input = new ActionBlock<FlowMessage>(async message =>
    {
        var value = message.Value.Json.GetInt32();
        if (value == 4) { Entered.TrySetResult(); await Release.Task; }
        Values.Add(value);
        Received.Post(message);
    });
    public ITargetBlock<FlowMessage> Input => _input;
    public Task Completion => _input.Completion;
    public void Complete() => _input.Complete();
    public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
    public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; Received.Complete(); }
}
