using FluxFlow.Json.Links;
using FluxFlow.Core.Addressing;
using FluxFlow.Mapping;
using FluxFlow.Core;

namespace FluxFlow.Composition.Links;

public sealed class CompiledApplicationLink
{
    private readonly Func<FlowMapContext, bool>? _condition;

    internal CompiledApplicationLink(
        ApplicationAddress source,
        ApplicationAddress target,
        Type messageType,
        string? conditionExpression,
        Func<FlowMapContext, bool>? condition,
        ApplicationLinkDeclarationSide declarationSide,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        bool isFallback = false)
    {
        Source = source;
        Target = target;
        MessageType = messageType;
        ConditionExpression = conditionExpression;
        _condition = condition;
        DeclarationSide = declarationSide;
        Delivery = delivery;
        if (isFallback && (conditionExpression is not null || condition is not null))
            throw new ArgumentException("Fallback links cannot have conditions.", nameof(isFallback));
        IsFallback = isFallback;
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public Type MessageType { get; }

    public string? ConditionExpression { get; }

    public bool IsConditional => _condition is not null;

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public FlowDeliveryPolicy Delivery { get; }

    public bool IsFallback { get; }

    /// <summary>Returns this compiled route with an explicit admission/failure policy.</summary>
    public CompiledApplicationLink WithDelivery(FlowDeliveryPolicy delivery)
    {
        if (!Enum.IsDefined(delivery))
            throw new ArgumentOutOfRangeException(nameof(delivery));
        return new(Source, Target, MessageType, ConditionExpression, _condition, DeclarationSide, delivery, IsFallback);
    }

    public bool IsMatch(FlowMapContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _condition?.Invoke(context) ?? true;
    }

    public bool TryMatch(FlowMapContext context, out Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            exception = null;
            return IsMatch(context);
        }
        catch (Exception caught)
        {
            exception = caught;
            return false;
        }
    }
}
