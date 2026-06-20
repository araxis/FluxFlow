namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttTriggerResponse
{
    public bool Succeeded { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public byte[]? Payload { get; init; }

    public string? ContentType { get; init; }

    public static MqttTriggerResponse Success(
        byte[]? payload = null,
        string? contentType = null)
        => new()
        {
            Succeeded = true,
            Payload = payload,
            ContentType = contentType
        };

    public static MqttTriggerResponse Failure(string? errorMessage = null)
        => new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage
        };
}
