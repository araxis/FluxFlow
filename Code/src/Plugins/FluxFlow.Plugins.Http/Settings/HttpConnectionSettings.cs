using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using Polly.Retry;

namespace FluxFlow.Configuration.Components.Http;

public static class HttpConnectionSettings
{
    public static (Uri? BaseAddress, Uri? TokenEndpoint, (string Key, string[] Values)[] Headers) Read(HttpClientConfiguration config)
    {
        var baseAddress = HttpClientConfiguration.ParseBaseAddress(config.BaseAddress);
        if (config.Timeout <= TimeSpan.Zero || config.Timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(config.Timeout));
        if (config.MaxResponseContentBufferSize is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(config.MaxResponseContentBufferSize));
        if (config.PooledConnectionLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(config.PooledConnectionLifetime));
        // Use native header parsing in both the editor and the runtime.
        using var validation = new HttpRequestMessage();
        Uri? tokenEndpoint = null;
        foreach (var header in config.Headers) validation.Headers.Add(header.Key, header.Value);
        if (config.Authentication is { } authentication)
        {
            if (validation.Headers.Contains("Authorization"))
                throw new ArgumentException("Specify Authentication or a default Authorization header, not both.");
            if (string.Equals(authentication.Scheme, "OAuthClientCredentials", StringComparison.OrdinalIgnoreCase))
                tokenEndpoint = ValidateOAuth(authentication);
            else
                validation.Headers.Authorization = ReadAuthentication(authentication);
        }
        var headers = validation.Headers.Select(header => (header.Key, Values: header.Value.ToArray())).ToArray();
        return (baseAddress, tokenEndpoint, headers);
    }

    public static T? Retry<T>(HttpRetryConfiguration? settings) where T : RetryStrategyOptions<HttpResponseMessage>, new()
    {
        if (settings is null) return null;
        ArgumentOutOfRangeException.ThrowIfNegative(settings.MaxRetryAttempts);
        if (!Enum.IsDefined(settings.BackoffType)) throw new ArgumentOutOfRangeException(nameof(settings.BackoffType));
        var retry = new T
        {
            MaxRetryAttempts = Math.Max(1, settings.MaxRetryAttempts), Delay = settings.Delay,
            BackoffType = settings.BackoffType, UseJitter = settings.UseJitter, MaxDelay = settings.MaxDelay
        };
        Validator.ValidateObject(retry, new ValidationContext(retry), validateAllProperties: true);
        return settings.MaxRetryAttempts == 0 ? null : retry;
    }

    private static AuthenticationHeaderValue ReadAuthentication(HttpAuthenticationConfiguration config)
    {
        if (config.TokenEndpoint is not null || config.ClientId is not null || config.ClientSecret is not null || config.Scope is not null
            || config.ClientCredentialStyle != Duende.IdentityModel.Client.ClientCredentialStyle.AuthorizationHeader)
            throw new ArgumentException("OAuth settings require Scheme OAuthClientCredentials.");
        if (string.Equals(config.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(config.Username);
            ArgumentNullException.ThrowIfNull(config.Password);
            if (config.Username.Contains(':') || config.Token is not null)
                throw new ArgumentException("Basic authentication requires a username without ':' and a password, with no Token.");
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}")));
        }
        if (string.Equals(config.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(config.Token);
            if (config.Username is not null || config.Password is not null)
                throw new ArgumentException("Bearer authentication requires Token, with no Username or Password.");
            if (!AuthenticationHeaderValue.TryParse($"Bearer {config.Token}", out var header))
                throw new ArgumentException("Bearer authentication Token must be a valid HTTP header value.");
            return header;
        }
        throw new ArgumentException("Authentication Scheme must be Basic, Bearer or OAuthClientCredentials.");
    }

    private static Uri ValidateOAuth(HttpAuthenticationConfiguration config)
    {
        var endpoint = HttpAuthenticationConfiguration.ParseTokenEndpoint(config.TokenEndpoint);
        if (config.Username is not null || config.Password is not null || config.Token is not null)
            throw new ArgumentException("OAuthClientCredentials does not accept Username, Password or Token.");
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ClientSecret);
        if (!Enum.IsDefined(config.ClientCredentialStyle)) throw new ArgumentOutOfRangeException(nameof(config.ClientCredentialStyle));
        return endpoint;
    }
}
