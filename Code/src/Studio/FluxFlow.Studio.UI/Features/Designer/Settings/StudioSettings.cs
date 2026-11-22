namespace FluxFlow.Studio.UI.Features.Designer.Settings;

public sealed record StudioSettings
{
    public string Theme { get; init; } = "System";
    public PortColors PortColors { get; init; } = new();
    public FluxFlow.Environments.EnvironmentDefaults Environments { get; init; } = new();
    public FluxFlow.Conversations.ProposalConnection? ProposalConnection { get; init; }
    public FluxFlow.Conversations.Dictation.DictationSettings Dictation { get; init; } = new();

    public void Validate(IEnumerable<FluxFlow.Environments.IResourceEnvironmentAdapter>? adapters = null)
    {
        if (Theme is not ("Light" or "Dark" or "System")) throw new ArgumentException("Unknown theme.");
        if (PortColors is null) throw new ArgumentException("Port colors must be an object.");
        PortColors.Validate();
        if (Environments is null) throw new ArgumentException("Environment defaults must be an object.");
        Environments.Validate(adapters);
        ProposalConnection?.Validate();
        if (Dictation is null) throw new ArgumentException("Dictation settings must be an object.");
        Dictation.Validate();
    }
}
