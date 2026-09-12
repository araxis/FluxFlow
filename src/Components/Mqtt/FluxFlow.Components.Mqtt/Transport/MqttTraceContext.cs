using System.Diagnostics;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Transport;

internal static class MqttTraceContext
{
    private const string Parent = "traceparent";
    private const string State = "tracestate";

    internal static MqttPublishMessage Inject(MqttPublishMessage message)
    {
        var current = Activity.Current;
        if (current is not { IdFormat: ActivityIdFormat.W3C, IsStopped: false, Id: not null })
            return message;

        var properties = new Dictionary<string, string>(message.UserProperties, StringComparer.Ordinal)
        {
            [Parent] = current.Id
        };
        if (string.IsNullOrEmpty(current.TraceStateString)) properties.Remove(State);
        else properties[State] = current.TraceStateString;
        return message with { UserProperties = properties };
    }

    internal static FlowMessage Receive(MqttReceivedApplicationMessage message)
    {
        // Subscription workers inherit startup context, not the remote publisher's.
        using var suppressed = FlowTracing.SuppressContext();
        message.UserProperties.TryGetValue(Parent, out var parent);
        message.UserProperties.TryGetValue(State, out var state);
        Dictionary<string, string>? headers = null;
        if (ActivityContext.TryParse(parent, state, isRemote: true, out _))
        {
            headers = new(StringComparer.Ordinal) { [Parent] = parent! };
            if (!string.IsNullOrEmpty(state)) headers[State] = state;
        }
        return FlowMessage.Create(FlowValue.From(message), headers: headers);
    }
}
