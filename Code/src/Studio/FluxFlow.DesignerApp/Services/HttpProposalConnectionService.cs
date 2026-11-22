using System.Net.Http.Json;
using FluxFlow.Conversations;

namespace FluxFlow.DesignerApp.Services;

public sealed class HttpProposalConnectionService(HttpClient client) : IProposalConnectionService
{
    public Task<ProposalConnectionStatus> GetModelsAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
        SendAsync("api/proposals/models", connection, cancellationToken);

    public Task<ProposalConnectionStatus> TestAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
        SendAsync("api/proposals/test", connection, cancellationToken);

    private async Task<ProposalConnectionStatus> SendAsync(string path, ProposalConnection? connection, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(path, new ProposalConnectionRequest(connection),
            ProposalJsonContext.Default.ProposalConnectionRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ProposalJsonContext.Default.ProposalConnectionStatus, cancellationToken)
            ?? throw new InvalidOperationException("The host returned no connection details.");
    }
}
