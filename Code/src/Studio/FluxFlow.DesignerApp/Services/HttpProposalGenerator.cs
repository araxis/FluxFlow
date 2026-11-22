using System.Net.Http.Json;
using FluxFlow.Conversations;

namespace FluxFlow.DesignerApp.Services;

public sealed class HttpProposalGenerator(HttpClient client) : IProposalGenerator
{
    public async Task<ProposalResult> GenerateAsync(ProposalRequest request, Func<string, Task> onText, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/proposals/stream")
        { Content = JsonContent.Create(request, ProposalJsonContext.Transport.ProposalRequest) };
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var update = System.Text.Json.JsonSerializer.Deserialize(line, ProposalJsonContext.Default.ProposalUpdate)
                ?? throw new InvalidOperationException("The conversation host returned an empty update.");
            cancellationToken.ThrowIfCancellationRequested();
            if (update.Result is { } result) return result;
            if (update.Text is { } text) await onText(text);
        }
        throw new InvalidOperationException("The response ended before completion. No pending edits were applied.");
    }

    public async Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/proposals/generate", request,
            ProposalJsonContext.Transport.ProposalRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ProposalJsonContext.Default.ProposalResult, cancellationToken)
            ?? throw new InvalidOperationException("The proposal host returned no result.");
    }
}
