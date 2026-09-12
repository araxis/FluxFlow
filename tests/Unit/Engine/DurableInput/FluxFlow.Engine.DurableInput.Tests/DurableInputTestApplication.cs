using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.DurableInput.Tests;

internal sealed class DurableInputTestApplication : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private DurableInputTestApplication(
        ServiceProvider provider,
        FluxFlowApplication application,
        MessageRecorder recorder,
        NodeCatalog nodes)
    {
        _provider = provider;
        Application = application;
        Recorder = recorder;
        Nodes = nodes;
    }

    public FluxFlowApplication Application { get; }

    public MessageRecorder Recorder { get; }

    public NodeCatalog Nodes { get; }

    public static async ValueTask<DurableInputTestApplication> CreateAsync(
        string componentType = "test.durable-string-a",
        bool start = true,
        int inputCapacity = 8)
    {
        var recorder = new MessageRecorder();
        var nodes = new NodeCatalog();
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(Definition(componentType), options =>
        {
            options.StartWithHost = false;
            options.StopWithHost = false;
            options.InputCapacity = inputCapacity;
        });
        RegisterComponents(services, recorder, nodes);
        var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        if (start)
        {
            var result = await application.StartAsync();
            if (!result.IsApplied)
            {
                await provider.DisposeAsync();
                throw new InvalidOperationException(
                    $"The durable-input test application did not start: {result.Status}.");
            }
        }

        return new DurableInputTestApplication(provider, application, recorder, nodes);
    }

    public static ApplicationDefinition Definition(string componentType)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Handler": {
                    "Type": "{{componentType}}"{{(componentType is "test.durable-string-a" or "test.durable-string-b" ? ",\"DiscardUnmatched\":[\"Output\"]" : string.Empty)}}
                  }
                }
              }
            }
            """);

    public async ValueTask DisposeAsync()
    {
        Nodes.Blocking?.Release();
        await _provider.DisposeAsync();
    }

    private static void RegisterComponents(
        IServiceCollection services,
        MessageRecorder recorder,
        NodeCatalog nodes)
    {
        services.AddSingleton(new ComponentDescriptor("test.durable-integer", _ =>
        {
            nodes.IncompatibleCreations++;
            throw new InvalidOperationException("Invalid metadata must be rejected before construction.");
        }, inputs: [ComponentPorts.Metadata<int>("Input")], outputs: [ComponentPorts.Metadata<int>("Output")]));
        services.AddFluxFlowComponents().Advanced
            .AddDynamicComponent("test.durable-string-a", component =>
                component
                    .UseFactory(_ => StringNode(new RecordingNode("revision-a", recorder), nodes))
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output))
            .AddDynamicComponent("test.durable-string-b", component =>
                component
                    .UseFactory(_ => StringNode(new RecordingNode("revision-b", recorder), nodes))
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output))
            .AddDynamicComponent("test.durable-blocking", component =>
                component
                    .UseFactory(_ => CreateBlockingNode(nodes))
                    .HasInput("Input", static node => node.Input))
            .AddDynamicComponent("test.durable-signal", component =>
                component
                    .UseFactory(static _ => new SignalNode())
                    .HasSignalInput("Signal", static node => node.Signal));

    }

    private static RecordingNode StringNode(RecordingNode node, NodeCatalog nodes)
    {
        nodes.Recording = node;
        return node;
    }

    private static BlockingNode CreateBlockingNode(NodeCatalog nodes)
    {
        var node = new BlockingNode();
        nodes.Blocking = node;
        return node;
    }

    internal sealed class NodeCatalog
    {
        public int IncompatibleCreations { get; set; }

        public RecordingNode? Recording { get; set; }

        public BlockingNode? Blocking { get; set; }
    }

    internal sealed class MessageRecorder
    {
        private readonly ConcurrentQueue<RecordedMessage> _messages = new();
        private readonly SemaphoreSlim _available = new(0);

        public IReadOnlyList<RecordedMessage> Messages => _messages.ToArray();

        public void Add(string revision, FlowMessage message)
        {
            _messages.Enqueue(new RecordedMessage(revision, message));
            _available.Release();
        }

        public async ValueTask<RecordedMessage> WaitAsync(
            CancellationToken cancellationToken = default)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _messages.Last();
        }
    }

    internal sealed record RecordedMessage(string Revision, FlowMessage Message);

    internal sealed class RecordingNode(string revision, MessageRecorder recorder) :
        FlowNode
    {
        protected override bool HandlesErrors => true;

        protected override async Task ProcessAsync(FlowMessage message)
        {
            recorder.Add(revision, message);
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }

    internal sealed class BlockingNode : FlowNode
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingNode()
            : base(new FlowNodeOptions { InputCapacity = 1, OutputCapacity = 1 })
        {
        }

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task ProcessAsync(FlowMessage message)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(Stopping).ConfigureAwait(false);
        }
    }

    private sealed class SignalNode : IFlowNode
    {
        public ITargetBlock<FlowMessage> Signal { get; } = new ActionBlock<FlowMessage>(static _ => { });
        public Task Completion => Signal.Completion;
        public void Complete() => Signal.Complete();
        public void Fault(Exception exception) => Signal.Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
