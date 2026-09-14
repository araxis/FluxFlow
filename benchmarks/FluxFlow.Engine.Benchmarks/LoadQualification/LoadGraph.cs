using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

internal sealed class LoadGraph : IAsyncDisposable
{
    private readonly Forward[] _nodes;
    private readonly Sink[] _sinks;
    private readonly Func<FlowMessage, CancellationToken, ValueTask<bool>> _send;
    private readonly Func<CancellationToken, Task> _stop;
    private readonly Func<ValueTask> _dispose;
    private readonly Func<int?> _portBacklog;

    private LoadGraph(Forward[] nodes, Sink[] sinks,
        Func<FlowMessage, CancellationToken, ValueTask<bool>> send,
        Func<CancellationToken, Task> stop, Func<ValueTask> dispose, Func<int?> portBacklog)
        => (_nodes, _sinks, _send, _stop, _dispose, _portBacklog) = (nodes, sinks, send, stop, dispose, portBacklog);

    internal ValueTask<bool> SendAsync(FlowMessage message, CancellationToken token) => _send(message, token);
    internal Task StopAsync(CancellationToken token) => _stop(token);
    public ValueTask DisposeAsync() => _dispose();
    internal QueueSnapshot Queues() => new(_nodes.Sum(node => ((ActionBlock<FlowMessage>)node.Input).InputCount)
        + _sinks.Sum(sink => sink.Input.InputCount), _nodes.Sum(node => node.Active) + _sinks.Sum(sink => sink.Active), _portBacklog());

    internal static async Task<LoadGraph> StartAsync(LoadOptions options, LoadMeasurements measurements)
    {
        var nodes = Enumerable.Range(0, options.Hops).Select(_ => new Forward(options.Capacity)).ToArray();
        var sinks = Enumerable.Range(0, options.Fanout).Select(index => new Sink(options.Capacity,
            index == 0 ? options.SlowTargetMilliseconds : 0, message => measurements.Receive(index, message))).ToArray();
        try
        {
            return options.Updates ? await UpdatesAsync(options, nodes, sinks) : await DirectAsync(options, nodes, sinks);
        }
        catch (Exception error)
        {
            foreach (var component in nodes.Cast<IFlowComponent>().Concat(sinks)) component.Fault(error);
            foreach (var component in nodes.Cast<IFlowComponent>().Concat(sinks)) await component.DisposeAsync();
            throw;
        }
    }

    private static async Task<LoadGraph> DirectAsync(LoadOptions options, Forward[] nodes, Sink[] sinks)
    {
        var application = new FlowApplication(linkCapacity: options.Capacity);
        try
        {
            var workflow = application.AddWorkflow("main");
            var handles = nodes.Select((node, index) => workflow.Add($"hop{index}", node)).ToArray();
            for (var index = 1; index < handles.Length; index++) handles[index - 1].Output.LinkTo(handles[index].Input);
            for (var index = 0; index < sinks.Length; index++)
                handles[^1].Output.LinkTo(workflow.Add($"target{index}", sinks[index]).Input);
            await application.StartAsync();
            return new(nodes, sinks, (message, token) => application.SendAsync(handles[0].Input, message, token),
                application.StopAsync, application.DisposeAsync, static () => null);
        }
        catch { await application.DisposeAsync(); throw; }
    }

    private static async Task<LoadGraph> UpdatesAsync(LoadOptions options, Forward[] nodes, Sink[] sinks)
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var handles = nodes.Select((node, index) => workflow.AddComponent($"hop{index}",
            $"load.forward{index}", context =>
            {
                context.HasInput(node.Input);
                context.HasOutput(node.Output);
                context.HasEvents(node.Events);
                return ValueTask.FromResult<IFlowNode>(node);
            }, static _ => { }, static component => new Handle(component))).ToArray();
        for (var index = 1; index < handles.Length; index++) handles[index - 1].Output.LinkTo(handles[index].Input);
        for (var index = 0; index < sinks.Length; index++)
        {
            var sink = sinks[index];
            var target = workflow.AddComponent($"target{index}", $"load.sink{index}", context =>
            {
                context.HasInput(sink.Input);
                return ValueTask.FromResult<IFlowNode>(sink);
            }, static _ => { }, static component => new Handle(component));
            handles[^1].Output.LinkTo(target.Input);
        }
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(builder.Build(), settings =>
        {
            settings.StartWithHost = false;
            settings.InputCapacity = options.Capacity;
            settings.OutputCapacity = options.Capacity;
        });
        var provider = services.BuildServiceProvider();
        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            var started = await application.StartAsync();
            if (!started.IsApplied) throw new InvalidOperationException($"Update-host load graph did not start: {started.Status}.");
            var address = handles[0].Input.Address;
            return new(nodes, sinks, async (message, token) =>
                (await application.Ports.SendAsync(address, message, token)).IsAccepted,
                token => application.StopAsync(token).AsTask(), provider.DisposeAsync,
                () => application.Ports.Status.Ports.Where(port => port.Address.Segments[0] == "main").Sum(port => port.PendingMessages));
        }
        catch { await provider.DisposeAsync(); throw; }
    }

    private sealed class Handle(ComponentHandle definition) : AuthoredComponentHandle(definition)
    {
        internal InputPortHandle<FlowValue> Input => Definition.Input<FlowValue>("Input");
        internal OutputPortHandle<FlowValue> Output => Definition.Output<FlowValue>("Output");
    }

    private sealed class Forward(int capacity) : FlowNode(new() { InputCapacity = capacity, OutputCapacity = capacity })
    {
        private int _active;
        internal int Active => Volatile.Read(ref _active);
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Interlocked.Increment(ref _active);
            try { await EmitAsync(message, Stopping); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly CancellationTokenSource _stopping = new();
        private int _active;
        private int _disposed;
        internal int Active => Volatile.Read(ref _active);
        internal ActionBlock<FlowMessage> Input { get; }
        internal Sink(int capacity, int delayMilliseconds, Action<FlowMessage> receive)
        {
            Input = new(async message =>
            {
                Interlocked.Increment(ref _active);
                try
                {
                    if (delayMilliseconds != 0) await Task.Delay(delayMilliseconds, _stopping.Token);
                    receive(message);
                }
                finally { Interlocked.Decrement(ref _active); }
            }, new() { BoundedCapacity = capacity });
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Input });
        }
        public FlowPorts Ports { get; }
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception exception)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _stopping.Cancel();
            ((IDataflowBlock)Input).Fault(exception);
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Complete();
            try { await Completion; }
            catch { /* Completion is the authoritative failure surface. */ }
            finally { _stopping.Dispose(); }
        }
    }
}

internal sealed record QueueSnapshot(int ComponentQueued, int ActiveHandlers, int? OptionalPortPending);
