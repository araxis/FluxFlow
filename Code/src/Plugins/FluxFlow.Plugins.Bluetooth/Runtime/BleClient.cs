using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Bluetooth;

public sealed class BleClient : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public BleClient(IBlePlatform platform, BleClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(platform);
        options.Validate();
        Output = new(static value => value, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static value => value);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message =>
            message.ProcessMessageAsync(this, ExecuteAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();

        async Task<IEnumerable<FlowMessage>> ExecuteAsync(FlowMessage message)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var operation = options.UseInputOperation ? BleOperation.FromJson(message.Value.Json) : options.ConfiguredOperation;
            operation.Validate();
            var target = options.UseInputOperation
                ? options with { UseInputOperation = false, Operation = operation.Operation, WriteMode = operation.WriteMode,
                    Value = operation.Value, ServiceId = operation.ServiceId, CharacteristicId = operation.CharacteristicId }
                : options;
            if (operation.Operation == BleOperationKind.Write)
            {
                var value = operation.Value!.Decode();
                await platform.WriteAsync(target, value, operation.WriteMode, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return [message.WithValue(new BleWriteResult(value.Length, operation.WriteMode))];
            }
            var bytes = await platform.ReadAsync(target, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return [message.WithValue(new BleReadResult(Convert.ToBase64String(bytes), bytes.Length))];
        }
    }
}
