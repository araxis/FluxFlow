using FluxFlow.Core.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>Sends each input to a port, awaits its correlated reply, and emits that reply on Output.</summary>
public sealed class CallFunction : FlowTransformNode
{
    private FlowApplication? _application;
    private FlowInputEndpoint? _request;
    private FlowOutputEndpoint? _reply;

    public CallFunction(string requestTo, string replyFrom, TimeSpan timeout, FlowNodeOptions? options = null)
        : base(ValidateOptions(requestTo, replyFrom, timeout, options))
        => (RequestTo, ReplyFrom, Timeout) = (requestTo, replyFrom, timeout);

    public string RequestTo { get; }
    public string ReplyFrom { get; }
    public TimeSpan Timeout { get; }

    internal (FlowComponent Request, FlowComponent Reply) Bind(FlowApplication application, string workflow)
    {
        _request = application.GetInput(ApplicationAddress.ResolvePort(RequestTo, workflow).Value);
        _reply = application.GetOutput(ApplicationAddress.ResolvePort(ReplyFrom, workflow).Value);
        _application = application;
        return (_request.Owner, _reply.Owner);
    }

    protected override async Task<FlowMessage> TransformAsync(FlowMessage message)
    {
        if (_application is null) throw new InvalidOperationException("CallFunction must belong to a running FlowApplication.");
        var reply = await _application.RequestAsync(_request!, _reply!, message, Timeout, Stopping).ConfigureAwait(false);
        // The inner exchange has its own correlation; restore the caller's exchange on our output.
        return reply.IsError ? message.WithError(reply.Error!, reply.Headers, reply.MessageId)
            : message.With(reply.Value!, reply.Headers, reply.MessageId);
    }

    private static FlowNodeOptions? ValidateOptions(string requestTo, string replyFrom, TimeSpan timeout, FlowNodeOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestTo);
        ArgumentException.ThrowIfNullOrWhiteSpace(replyFrom);
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan &&
            (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return options;
    }
}
