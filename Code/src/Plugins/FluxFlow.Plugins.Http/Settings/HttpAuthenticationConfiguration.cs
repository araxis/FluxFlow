using Duende.IdentityModel.Client;

namespace FluxFlow.Configuration.Components.Http;

public sealed class HttpAuthenticationConfiguration
{
    public required string Scheme { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Token { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scope { get; init; }
    public ClientCredentialStyle ClientCredentialStyle { get; init; } = ClientCredentialStyle.AuthorizationHeader;

    public static Uri ParseTokenEndpoint(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("TokenEndpoint must be HTTPS (HTTP is permitted on loopback), without credentials or a fragment.");
        return uri;
    }
}
