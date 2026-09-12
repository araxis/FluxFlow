using FluxFlow.Core.Addressing;
using FluxFlow.Composition;

namespace FluxFlow.Engine.Internal.Revisions;

internal sealed class ApplicationRevisionPlan
{
    internal ApplicationRevisionPlan(
        ApplicationSetup current,
        ApplicationSetup next,
        IReadOnlyList<ApplicationResourceRevisionChange> resourceChanges,
        IReadOnlyList<ApplicationWorkflowRevisionChange> workflowChanges,
        IReadOnlyList<ApplicationAddress> affectedResources,
        IReadOnlyList<string> affectedWorkflows,
        IReadOnlyList<ApplicationRevisionDiagnostic> diagnostics)
    {
        Current = current;
        Next = next;
        ResourceChanges = resourceChanges;
        WorkflowChanges = workflowChanges;
        AffectedResources = affectedResources;
        AffectedWorkflows = affectedWorkflows;
        Diagnostics = diagnostics;
    }

    public ApplicationSetup Current { get; }

    public ApplicationSetup Next { get; }

    public IReadOnlyList<ApplicationResourceRevisionChange> ResourceChanges { get; }

    public IReadOnlyList<ApplicationWorkflowRevisionChange> WorkflowChanges { get; }

    public IReadOnlyList<ApplicationAddress> AffectedResources { get; }

    public IReadOnlyList<string> AffectedWorkflows { get; }

    public IReadOnlyList<ApplicationRevisionDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public bool HasChanges => ResourceChanges.Count != 0 || WorkflowChanges.Count != 0 ||
        !string.Equals(Current.Definition.ExpressionEngine, Next.Definition.ExpressionEngine, StringComparison.Ordinal);
}
