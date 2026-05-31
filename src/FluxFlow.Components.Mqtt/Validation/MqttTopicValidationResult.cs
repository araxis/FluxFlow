namespace FluxFlow.Components.Mqtt.Validation;

public sealed record MqttTopicValidationResult
{
    private MqttTopicValidationResult(bool isValid, string? message)
    {
        IsValid = isValid;
        Message = message;
    }

    public bool IsValid { get; }

    public string? Message { get; }

    public static MqttTopicValidationResult Valid { get; } = new(true, null);

    public static MqttTopicValidationResult Invalid(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return new MqttTopicValidationResult(false, message);
    }
}
