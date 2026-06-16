using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscribeNode : SourceFlowNode<MqttReceivedMessage>, IFlowEventSource, IAsyncDisposable
{
    private const string NotConnectedMessage =
        "MQTT subscribe node is not connected; the mqtt.connection component does not establish a client yet.";

    private readonly object _stateLock = new();
    private readonly IMqttConnectionHandle _connection;
    private readonly MqttSubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private bool _started;
    private bool _disposed;

    internal MqttSubscribeNode(
        MqttSubscriptionOptions options,
        IMqttConnectionHandle connection,
        TimeProvider clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("MQTT subscribe node has already started.");
            }

            _started = true;
        }

        // The mqtt.connection component holds configuration only; no client is
        // established yet, so the node cannot open a subscription and reports
        // not connected. No messages are produced.
        ReportSubscribeError(
            MqttErrorCodes.SubscribeNotConnected,
            NotConnectedMessage);
        TryEmitDiagnostic(
            MqttDiagnosticNames.SubscribeFailed,
            FlowDiagnosticLevel.Error,
            NotConnectedMessage,
            attributes: CreateSubscriptionAttributes());

        return Task.CompletedTask;
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        base.Fault(exception);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        Complete();
        return ValueTask.CompletedTask;
    }

    protected override void OnNodeCompleted()
    {
        _events.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_events).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private void ReportSubscribeError(int code, string message)
        => TryReportError(
            code,
            message,
            context: CreateSubscriptionContext());

    private string CreateSubscriptionContext()
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.TopicFilter))
        {
            values.Add($"topicFilter={_options.TopicFilter}");
        }

        values.Add($"qualityOfService={_options.QualityOfService}");
        values.Add($"receiveRetainedMessages={_options.ReceiveRetainedMessages}");
        values.Add($"retainAsPublished={_options.RetainAsPublished}");
        values.Add($"connectionName={_connection.ConnectionName}");

        return string.Join("; ", values);
    }

    private Dictionary<string, object?> CreateSubscriptionAttributes()
        => new()
        {
            ["topicFilter"] = _options.TopicFilter,
            ["qualityOfService"] = _options.QualityOfService,
            ["receiveRetainedMessages"] = _options.ReceiveRetainedMessages,
            ["retainAsPublished"] = _options.RetainAsPublished,
            ["connectionName"] = _connection.ConnectionName
        };
}
