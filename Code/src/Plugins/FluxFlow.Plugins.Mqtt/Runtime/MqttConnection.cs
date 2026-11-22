using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Mqtt;

public sealed class MqttConnection : FlowNode, IFlowSource
{
    private readonly MqttClientResource _connection;
    public ITargetBlock<FlowMessage> Input { get; }
    internal CancellationToken ConnectionStopping { get; }

    public MqttConnection(MqttConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        _connection = options.Connection;
        Output = new(static message => message, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static message => message);
        Lifetime = new FlowLifetime(this, flush: _connection.StopAsync,
            produce: token => _connection.RunAsync(EmitErrorAsync, token));
        ConnectionStopping = Stopping;
        _connection.ClaimConnection(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        _connection.StateChanged += OnStateChanged;
        Lifetime.Observe();
    }

    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
    public void StopProduction() => Lifetime.StopProduction();

    private async Task<IEnumerable<FlowMessage>> ProcessAsync(FlowMessage message)
    {
        var command = MqttJson.Read<MqttConnectionCommand>(message);
        var connected = await _connection.ExecuteCommandAsync(command, Stopping).ConfigureAwait(false);
        return [message.WithValue(MqttJson.Value(new { Command = command.Command.ToString(), Connected = connected }))];
    }

    private void OnStateChanged(FlowEvent state) => EmitEvent(state);

    protected override ValueTask OnDisposeAsync()
    {
        _connection.StateChanged -= OnStateChanged;
        _connection.ReleaseConnection(this);
        return ValueTask.CompletedTask;
    }
}
