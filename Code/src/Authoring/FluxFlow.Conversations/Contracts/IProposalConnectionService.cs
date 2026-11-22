namespace FluxFlow.Conversations;

public interface IProposalConnectionService
{
    Task<ProposalConnectionStatus> GetModelsAsync(ProposalConnection? connection, CancellationToken cancellationToken = default);
    Task<ProposalConnectionStatus> TestAsync(ProposalConnection? connection, CancellationToken cancellationToken = default);
}

public sealed record ProposalConnection
{
    public string Endpoint { get; init; } = "http://127.0.0.1:11434/";
    public string Model { get; init; } = "";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
            uri.Scheme != Uri.UriSchemeHttp || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use a local HTTP address with a port, for example http://127.0.0.1:11434/.");
        if (Model is null) throw new ArgumentException("Model must be text.");
    }
}

public sealed record ProposalConnectionRequest(ProposalConnection? Connection = null);

public sealed record ProposalConnectionStatus
{
    public ProposalConnection Connection { get; init; } = new();
    public string[] Models { get; init; } = [];
    public string? Error { get; init; }
}
