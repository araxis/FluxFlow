using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class ApplicationResourceRegistrationContext
{
    public ApplicationResourceRegistrationContext(
        ApplicationDefinition definition,
        long sequence,
        string revisionId,
        IServiceProvider hostServices,
        IServiceCollection services)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        Sequence = sequence;
        RevisionId = revisionId;
        HostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ApplicationDefinition Definition { get; }

    public long Sequence { get; }

    public string RevisionId { get; }

    public IServiceProvider HostServices { get; }

    public IServiceCollection Services { get; }
}
