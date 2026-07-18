using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Revisions;

public sealed class ApplicationRevisionPlan
{
    internal ApplicationRevisionPlan(
        ApplicationDefinition current,
        ApplicationDefinition next,
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

    public ApplicationDefinition Current { get; }

    public ApplicationDefinition Next { get; }

    public IReadOnlyList<ApplicationResourceRevisionChange> ResourceChanges { get; }

    public IReadOnlyList<ApplicationWorkflowRevisionChange> WorkflowChanges { get; }

    public IReadOnlyList<ApplicationAddress> AffectedResources { get; }

    public IReadOnlyList<string> AffectedWorkflows { get; }

    public IReadOnlyList<ApplicationRevisionDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public bool HasChanges => ResourceChanges.Count != 0 || WorkflowChanges.Count != 0;
}
