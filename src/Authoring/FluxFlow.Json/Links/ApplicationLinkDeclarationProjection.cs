using FluxFlow.Core.Addressing;
using FluxFlow.Core;
using System.Text.Json;

namespace FluxFlow.Json.Links;

/// <summary>
/// A resolved canonical link declaration suitable for persistence and design-time projection.
/// </summary>
public sealed class ApplicationLinkDeclarationProjection
{
    public ApplicationLinkDeclarationProjection(
        ApplicationAddress source,
        ApplicationAddress target,
        string? conditionExpression = null,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        bool isFallback = false)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
        Delivery = delivery;
        if (!CanProject(source, target, declarationSide))
        {
            throw new ArgumentException(
                "Link declarations require a workflow or system output source, a workflow input target, and a workflow declaration port.",
                nameof(source));
        }

        if (conditionExpression is not null && string.IsNullOrWhiteSpace(conditionExpression))
        {
            throw new ArgumentException(
                "Link conditions cannot be empty.",
                nameof(conditionExpression));
        }

        ConditionExpression = conditionExpression;
        Parameters = ConditionParameters.Copy(parameters);
        if (isFallback && (conditionExpression is not null || Parameters.Count > 0))
            throw new ArgumentException("Fallback links cannot have conditions or parameters.", nameof(isFallback));
        IsFallback = isFallback;
        if (Parameters.Count > 0 && conditionExpression is null)
            throw new ArgumentException("Condition parameters require an expression.", nameof(parameters));
        DeclarationSide = declarationSide;
        DeclaredPort = declarationSide == ApplicationLinkDeclarationSide.Output
            ? source
            : target;
        Reference = declarationSide == ApplicationLinkDeclarationSide.Output
            ? target
            : source;
        DeclarationLocation = $"Workflows.{string.Join('.', DeclaredPort.Segments)}";
        PortReference = ToPortReference(Reference, DeclaredPort.Segments[0]);
    }

    public ApplicationAddress Source { get; }

    public ApplicationAddress Target { get; }

    public string? ConditionExpression { get; }

    public IReadOnlyDictionary<string, JsonElement> Parameters { get; }

    public ApplicationLinkDeclarationSide DeclarationSide { get; }

    public FlowDeliveryPolicy Delivery { get; }

    public bool IsFallback { get; }

    public ApplicationAddress DeclaredPort { get; }

    public ApplicationAddress Reference { get; }

    public string DeclarationLocation { get; }

    public string PortReference { get; }

    internal static bool CanProject(
        ApplicationAddress source,
        ApplicationAddress target,
        ApplicationLinkDeclarationSide declarationSide)
        => declarationSide is ApplicationLinkDeclarationSide.Input or ApplicationLinkDeclarationSide.Output &&
           source.Kind is ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort &&
           target.Kind == ApplicationAddressKind.WorkflowPort &&
           (declarationSide != ApplicationLinkDeclarationSide.Output ||
            source.Kind == ApplicationAddressKind.WorkflowPort);

    private static string ToPortReference(
        ApplicationAddress address,
        string currentWorkflow)
        => address.Kind == ApplicationAddressKind.WorkflowPort &&
           string.Equals(address.Segments[0], currentWorkflow, StringComparison.Ordinal)
            ? $"{address.Segments[1]}.{address.Segments[2]}"
            : address.Value;
}
