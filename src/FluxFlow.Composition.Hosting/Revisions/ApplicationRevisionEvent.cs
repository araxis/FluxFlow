using FluxFlow.Composition.Addressing;
using FluxFlow.Data;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed record ApplicationRevisionEvent
{
    public ApplicationRevisionEvent(
        long sequence,
        string revisionId,
        DateTimeOffset timestamp,
        ApplicationRevisionPhase phase,
        IEnumerable<ApplicationAddress>? resources = null,
        IEnumerable<string>? workflows = null,
        FlowError? error = null)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Revision sequence must be greater than zero.");
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        if (!Enum.IsDefined(phase))
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown revision phase.");

        Sequence = sequence;
        RevisionId = revisionId.Trim();
        Timestamp = timestamp;
        Phase = phase;
        Resources = CopyResources(resources);
        Workflows = CopyWorkflows(workflows);
        Error = error;
    }

    public long Sequence { get; }

    public string RevisionId { get; }

    public DateTimeOffset Timestamp { get; }

    public ApplicationRevisionPhase Phase { get; }

    public IReadOnlyList<ApplicationAddress> Resources { get; }

    public IReadOnlyList<string> Workflows { get; }

    public FlowError? Error { get; }

    private static IReadOnlyList<ApplicationAddress> CopyResources(
        IEnumerable<ApplicationAddress>? resources)
        => resources is null
            ? []
            : resources
                .Select(static resource => resource ?? throw new ArgumentException(
                    "Revision resources cannot contain null entries.",
                    nameof(resources)))
                .Distinct()
                .OrderBy(static resource => resource.Value, StringComparer.Ordinal)
                .ToArray();

    private static IReadOnlyList<string> CopyWorkflows(IEnumerable<string>? workflows)
        => workflows is null
            ? []
            : workflows
                .Select(static workflow => string.IsNullOrWhiteSpace(workflow)
                    ? throw new ArgumentException(
                        "Revision workflows cannot contain blank entries.",
                        nameof(workflows))
                    : workflow.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static workflow => workflow, StringComparer.Ordinal)
                .ToArray();
}
