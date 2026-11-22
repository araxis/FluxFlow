using System.Net.Http.Json;
using System.Text.Json;
using FluxFlow.Conversations;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Conversations.Local;

public sealed class LocalProposalConnectionService(HttpClient client, IConfiguration configuration) : IProposalConnectionService
{
    public Task<ProposalConnectionStatus> GetModelsAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
        CheckAsync(connection, false, cancellationToken);

    public Task<ProposalConnectionStatus> TestAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
        CheckAsync(connection, true, cancellationToken);

    internal static ProposalConnection Resolve(ProposalConnection? connection, IConfiguration configuration)
    {
        connection ??= new()
        {
            Endpoint = configuration["Proposals:Endpoint"] ?? "http://127.0.0.1:11434/",
            Model = configuration["Proposals:Model"] ?? ""
        };
        connection.Validate();
        return connection;
    }

    private async Task<ProposalConnectionStatus> CheckAsync(ProposalConnection? connection, bool test, CancellationToken cancellationToken)
    {
        var status = new ProposalConnectionStatus();
        try
        {
            connection = Resolve(connection, configuration);
            status = status with { Connection = connection };
            using var response = await client.GetAsync(new Uri(new Uri(connection.Endpoint), "/api/tags"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var listing = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var models = listing.RootElement.GetProperty("models").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString() ?? throw new JsonException("An installed model has no name."))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            status = status with { Models = models };
            if (!test) return status;
            if (string.IsNullOrWhiteSpace(connection.Model)) throw new ArgumentException("Select a model first.");
            if (!models.Contains(connection.Model, StringComparer.Ordinal))
                throw new ArgumentException("The selected model is not installed at this endpoint. Refresh the list and select an installed model.");

            using var check = await client.PostAsJsonAsync(new Uri(new Uri(connection.Endpoint), "/api/chat"), new
            {
                model = connection.Model, stream = false, think = false,
                messages = new[] { new { role = "user", content = "Reply with OK." } },
                options = new { temperature = 0, num_predict = 8 }
            }, cancellationToken);
            check.EnsureSuccessStatusCode();
            using var reply = await JsonDocument.ParseAsync(await check.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(reply.RootElement.GetProperty("message").GetProperty("content").GetString()))
                throw new JsonException("The selected model returned no text.");
            cancellationToken.ThrowIfCancellationRequested();
            return status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return status with { Error = "The connection test timed out. Check the local service and try again." }; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or FormatException or HttpRequestException or KeyNotFoundException)
        { return status with { Error = error.Message }; }
    }
}
