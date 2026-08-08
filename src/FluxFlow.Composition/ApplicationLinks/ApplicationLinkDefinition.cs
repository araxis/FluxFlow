using System.Runtime.CompilerServices;
using FluxFlow.Composition.Addressing;
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
        ApplicationLinkDeclarationSide declarationSide)
    {
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

    internal Func<FlowMapContext, bool>? CodeCondition { get; }

    internal static ApplicationLinkDefinition Unconditional<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        ApplicationLinkDeclarationSide declarationSide)
        => new(
            source,
            target,
            typeof(TMessage),
            conditionExpression: null,
            codeCondition: null,
            conditionIdentity: null,
            declarationSide);

    internal static ApplicationLinkDefinition Expression<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        string condition,
        ApplicationLinkDeclarationSide declarationSide)
        => new(
            source,
            target,
            typeof(TMessage),
            condition,
            codeCondition: null,
            conditionIdentity: null,
            declarationSide);

    internal static ApplicationLinkDefinition Predicate<TMessage>(
        ApplicationAddress source,
        ApplicationAddress target,
        Func<TMessage, bool> when,
        ApplicationLinkDeclarationSide declarationSide)
    {
        ArgumentNullException.ThrowIfNull(when);
        return new(
            source,
            target,
            typeof(TMessage),
            conditionExpression: null,
            context => Match(context, when),
            new object(),
            declarationSide);
    }

    public bool Equals(ApplicationLinkDefinition? other)
        => other is not null &&
           Source == other.Source &&
           Target == other.Target &&
           MessageType == other.MessageType &&
           string.Equals(ConditionExpression, other.ConditionExpression, StringComparison.Ordinal) &&
           ReferenceEquals(_conditionIdentity, other._conditionIdentity) &&
           DeclarationSide == other.DeclarationSide;

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
        return hash.ToHashCode();
    }

    private static bool Match<TMessage>(FlowMapContext context, Func<TMessage, bool> when)
    {
        if (!context.Variables.TryGetValue("message", out var candidate) ||
            candidate is not FlowMessage<TMessage> message)
        {
            throw new InvalidOperationException(
                $"Link condition expected a FlowMessage<{typeof(TMessage)}> context.");
        }

        return !message.IsError && when(message.Value);
    }
}
