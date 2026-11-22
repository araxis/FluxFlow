namespace FluxFlow.Components.Mqtt;

public sealed record MqttTopic
{
    public required string Topic { get; init; }
    public int QoS { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrEmpty(Topic);
        if (Topic.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(Topic) > 65535)
            throw new ArgumentException("Invalid MQTT topic filter.", nameof(Topic));
        var levels = Topic.Split('/');
        for (var i = 0; i < levels.Length; i++)
            if ((levels[i].Contains('#') && (levels[i] != "#" || i != levels.Length - 1))
                || (levels[i].Contains('+') && levels[i] != "+"))
                throw new ArgumentException("MQTT wildcards must occupy a whole level; # must be last.", nameof(Topic));
        if (QoS is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(QoS));
    }
}

