using System.Collections.Concurrent;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
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
        services.AddFluxFlow(Definition(componentType), options =>
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
                    "Type": "{{componentType}}"
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
        => services.AddFluxFlowComponents().Advanced
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
            .AddDynamicComponent("test.durable-integer", component =>
                component
                    .UseFactory(static _ => new IntegerNode())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output))
            .AddDynamicComponent("test.durable-blocking", component =>
                component
                    .UseFactory(_ => CreateBlockingNode(nodes))
                    .HasInput("Input", static node => node.Input))
            .AddDynamicComponent("test.durable-signal", component =>
                component
                    .UseFactory(static _ => new SignalNode())
                    .HasSignalInput("Signal", static node => node));

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
        public RecordingNode? Recording { get; set; }

        public BlockingNode? Blocking { get; set; }
    }

    internal sealed class MessageRecorder
    {
        private readonly ConcurrentQueue<RecordedMessage> _messages = new();
        private readonly SemaphoreSlim _available = new(0);

        public IReadOnlyList<RecordedMessage> Messages => _messages.ToArray();

        public void Add(string revision, FlowMessage<string> message)
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

    internal sealed record RecordedMessage(string Revision, FlowMessage<string> Message);

    internal sealed class RecordingNode(string revision, MessageRecorder recorder) :
        FlowNode<string, string>
    {
        protected override bool HandlesErrors => true;

        protected override async Task ProcessAsync(FlowMessage<string> message)
        {
            recorder.Add(revision, message);
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }

    internal sealed class IntegerNode : FlowNode<int, int>
    {
        protected override async Task ProcessAsync(FlowMessage<int> message)
            => await EmitAsync(message, Stopping).ConfigureAwait(false);
    }

    internal sealed class BlockingNode : FlowNode<string, string>
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

        protected override async Task ProcessAsync(FlowMessage<string> message)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(Stopping).ConfigureAwait(false);
        }
    }

    private sealed class SignalNode : IFlowNode, IFlowSignalTarget
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }
}
