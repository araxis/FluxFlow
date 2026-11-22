using System.Net.Http.Headers;
using Duende.AccessTokenManagement;

namespace FluxFlow.Configuration.Components.Http;

/// <summary>Applies managed tokens while preserving explicit request authentication and refusing unauthenticated sends.</summary>
internal sealed class HttpOAuthHandler(IClientCredentialsTokenManager tokens, ClientCredentialsClientName clientName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is not null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var result = await tokens.GetAccessTokenAsync(clientName, ct: cancellationToken).ConfigureAwait(false);
        if (!result.WasSuccessful(out var token))
            throw new InvalidOperationException("OAuth client-credentials token acquisition failed.");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        try { return await base.SendAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { request.Headers.Authorization = null; } // Each retry checks the managed token's expiration again.
    }
}
