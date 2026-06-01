namespace FluxFlow.MqttCompositionSample;

internal sealed record IncomingOrder(
    string Id,
    string Customer,
    decimal Total,
    bool Active);

internal sealed record OrderMessage(
    string Id,
    string Customer,
    decimal Total,
    bool Active,
    bool Priority,
    string SourceTopic,
    string? CorrelationId);

internal sealed record ReviewPayload(
    string Id,
    string Customer,
    decimal Total,
    bool Priority);
