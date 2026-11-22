using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed class StudioEditProposal
{
    internal StudioEditProposal(DesignerGraphState owner, long revision, string configuration,
        IReadOnlyList<ValidationMessageModel> diagnostics)
    {
        Owner = owner;
        Revision = revision;
        Configuration = configuration;
        Diagnostics = diagnostics;
    }

    internal DesignerGraphState Owner { get; }
    public long Revision { get; }
    public string Configuration { get; }
    public IReadOnlyList<ValidationMessageModel> Diagnostics { get; }
}
