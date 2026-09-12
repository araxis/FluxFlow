using FluxFlow.Json.Links;
using System.Runtime.CompilerServices;
using FluxFlow.Core.Addressing;
using FluxFlow.Core;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkDefinition : IEquatable<ApplicationLinkDefinition>
{
    private readonly object? _conditionIdentity;

    private ApplicationLinkDefinition(
        ApplicationAddress source,
        ApplicationAddress target,
        Type messageType,
        string? conditionExpression,
        Func<FlowMapContext, bool>? codeCondition,
        object? conditionIdentity,
        ApplicationLinkDeclarationSide declarationSide,
        FlowDeliveryPolicy delivery,
        bool isFallback = false)
    {
        if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
        Delivery = delivery;
        IsFallback = isFallback;
        Source = source;
        Target = target;
        MessageType = messageType;
        ConditionExpression = conditionExpression;
        CodeCondition = codeCondition;
        _conditionIdentity = conditionIdentity;
        DeclarationSide = declarationSide;
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public Type MessageType { get; }

    public string? ConditionExpression { get; }

    public bool IsConditional => ConditionExpression is not null || CodeCondition is not null;

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public FlowDeliveryPolicy Delivery { get; }

    public bool IsFallback { get; }

    internal static ApplicationLinkDefinition Fallback<TMessage>(ApplicationAddress source,
        ApplicationAddress target, ApplicationLinkDeclarationSide declarationSide, FlowDeliveryPolicy delivery)
        => new(source, target, typeof(TMessage), null, null, null, declarationSide, delivery, isFallback: true);

    internal Func<FlowMapContext, bool>? CodeCondition { get; }

    internal static ApplicationLinkDefinition Unconditional<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        ApplicationLinkDeclarationSide declarationSide,
        FlowDeliveryPolicy delivery)
        => new(
            source,
            target,
            typeof(TMessage),
            conditionExpression: null,
            codeCondition: null,
            conditionIdentity: null,
            declarationSide, delivery);

    internal static ApplicationLinkDefinition Expression<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        string condition,
        ApplicationLinkDeclarationSide declarationSide,
        FlowDeliveryPolicy delivery)
        => new(
            source,
            target,
            typeof(TMessage),
            condition,
            codeCondition: null,
            conditionIdentity: null,
            declarationSide, delivery);

    internal static ApplicationLinkDefinition Predicate<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        Func<TMessage, bool> when,
        ApplicationLinkDeclarationSide declarationSide,
        FlowDeliveryPolicy delivery)
    {
        ArgumentNullException.ThrowIfNull(when);
        return new(
            source,
            target,
            typeof(TMessage),
            conditionExpression: null,
            context => Match(context, when),
            new object(),
            declarationSide, delivery);
    }

    public bool Equals(ApplicationLinkDefinition? other)
        => other is not null &&
           Source == other.Source &&
           Target == other.Target &&
           MessageType == other.MessageType &&
           string.Equals(ConditionExpression, other.ConditionExpression, StringComparison.Ordinal) &&
           ReferenceEquals(_conditionIdentity, other._conditionIdentity) &&
           DeclarationSide == other.DeclarationSide && Delivery == other.Delivery && IsFallback == other.IsFallback;

    public override bool Equals(object? obj) => Equals(obj as ApplicationLinkDefinition);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Source);
        hash.Add(Target);
        hash.Add(MessageType);
        hash.Add(ConditionExpression, StringComparer.Ordinal);
        hash.Add(_conditionIdentity is null ? 0 : RuntimeHelpers.GetHashCode(_conditionIdentity));
        hash.Add(DeclarationSide);
        hash.Add(Delivery);
        hash.Add(IsFallback);
        return hash.ToHashCode();
    }

    private static bool Match<TMessage>(FlowMapContext context, Func<TMessage, bool> when)
    {
        if (!context.Variables.TryGetValue("message", out var candidate))
        {
            throw new InvalidOperationException(
                $"Link condition expected a FlowMessage<{typeof(TMessage)}> context.");
        }

        if (candidate is FlowMessage<TMessage> typedMessage)
            return !typedMessage.IsError && when(typedMessage.Value);

        if (candidate is FlowMessage canonicalMessage)
        {
            if (canonicalMessage.IsError) return false;
            return when(typeof(TMessage) == typeof(global::FluxFlow.Data.FlowValue)
                ? (TMessage)(object)canonicalMessage.Value!
                : canonicalMessage.Value!.Deserialize<TMessage>()!);
        }

        throw new InvalidOperationException(
            $"Link condition expected a FlowMessage<{typeof(TMessage)}> context.");
    }
}
