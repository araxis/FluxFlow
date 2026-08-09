using FluxFlow.Composition.Addressing;
using FluxFlow.Mapping;

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
        ApplicationLinkDeclarationSide declarationSide)
    {
        Source = source;
        Target = target;
        MessageType = messageType;
        ConditionExpression = conditionExpression;
        _condition = condition;
        DeclarationSide = declarationSide;
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public Type MessageType { get; }

    public string? ConditionExpression { get; }

    public bool IsConditional => _condition is not null;

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

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
