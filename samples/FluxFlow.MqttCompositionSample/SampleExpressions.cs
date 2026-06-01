using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Mapping;
using System.Text;
using System.Text.Json;

namespace FluxFlow.MqttCompositionSample;

internal sealed class SampleExpressionEngine : IFlowExpressionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "sample";

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resultType);

        return expression.Trim() switch
        {
            "decode-order" => DecodeOrder(GetInput<MqttReceivedMessage>(context), resultType),
            "order-is-active" => GetInput<OrderMessage>(context).Active,
            "create-publish-request" => CreatePublishRequest(GetInput<OrderMessage>(context), resultType),
            _ => throw new InvalidOperationException($"Sample expression '{expression}' is not supported.")
        };
    }

    private static OrderMessage DecodeOrder(
        MqttReceivedMessage input,
        Type resultType)
    {
        if (resultType != typeof(OrderMessage))
        {
            throw new InvalidOperationException(
                $"decode-order expected result type '{nameof(OrderMessage)}'.");
        }

        var order = JsonSerializer.Deserialize<IncomingOrder>(input.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Incoming order payload was empty.");

        return new OrderMessage(
            order.Id,
            order.Customer,
            order.Total,
            order.Active,
            Priority: order.Total >= 100m,
            input.Topic,
            input.CorrelationId);
    }

    private static MqttPublishRequest CreatePublishRequest(
        OrderMessage input,
        Type resultType)
    {
        if (resultType != typeof(MqttPublishRequest))
        {
            throw new InvalidOperationException(
                $"create-publish-request expected result type '{nameof(MqttPublishRequest)}'.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ReviewPayload(input.Id, input.Customer, input.Total, input.Priority),
            JsonOptions);

        return new MqttPublishRequest
        {
            Topic = $"orders/reviewed/{input.Id}",
            Payload = payload,
            PayloadPreview = Encoding.UTF8.GetString(payload),
            ContentType = "application/json",
            QualityOfService = MqttQualityOfService.AtLeastOnce,
            Retain = false,
            CorrelationId = input.CorrelationId
        };
    }

    private static TInput GetInput<TInput>(FlowMapContext context)
        => context.Variables.TryGetValue("input", out var value) && value is TInput input
            ? input
            : throw new InvalidOperationException(
                $"Sample expression expected input type '{typeof(TInput).Name}'.");
}

internal sealed class MqttMessageContextFactory : IFlowMapContextFactory<MqttReceivedMessage>
{
    public FlowMapContext Create(MqttReceivedMessage input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["topic"] = input.Topic,
                ["payload"] = input.Payload,
                ["payloadText"] = Encoding.UTF8.GetString(input.Payload),
                ["qualityOfService"] = input.QualityOfService,
                ["retain"] = input.Retain,
                ["correlationId"] = input.CorrelationId
            }
        };
}

internal sealed class OrderMessageContextFactory : IFlowMapContextFactory<OrderMessage>
{
    public FlowMapContext Create(OrderMessage input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["orderId"] = input.Id,
                ["customer"] = input.Customer,
                ["total"] = input.Total,
                ["active"] = input.Active,
                ["priority"] = input.Priority,
                ["sourceTopic"] = input.SourceTopic,
                ["correlationId"] = input.CorrelationId
            }
        };
}
