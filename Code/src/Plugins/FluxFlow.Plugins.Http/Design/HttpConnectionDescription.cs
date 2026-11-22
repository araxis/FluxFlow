using FluxFlow.Authoring;
using System.Text.Json;
using Duende.IdentityModel.Client;
using FluxFlow.Configuration.Components.Http;
using Polly;
using static FluxFlow.Authoring.OptionEditorModel;
using Microsoft.Extensions.Configuration;
using Polly.Retry;
using static FluxFlow.Plugins.PropertyGroup;

namespace FluxFlow.Plugins.Http;

public static class HttpConnectionDescription
{
    public static PropertyRules Rules { get; } = new()
    {
        Groups = _ => [
            new("Connection", null, [Address("BaseAddress", "Address", HttpClientConfiguration.ParseBaseAddress),
                Create("Timeout", "Timeout", OptionEditorKind.Duration, defaultValue: "00:01:40") with
                    { MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(int.MaxValue) },
                Create("AllowAutoRedirect", "Redirects", OptionEditorKind.Toggle, defaultValue: false),
                Create("PooledConnectionLifetime", "Pool lifetime", OptionEditorKind.Duration, defaultValue: "00:02:00") with { MinDuration = TimeSpan.FromTicks(1) },
                Create("MaxResponseContentBufferSize", "Response limit", OptionEditorKind.Integer, "Bytes.", 10485760) with { Min = 1, Max = int.MaxValue }])
                { StringMaps = [new("Headers", "Headers", "header") { Comparer = StringComparer.OrdinalIgnoreCase, SecretValues = true, Validate = ValidateHeader }] },
            new("Authentication", "Authentication", [Select("Scheme", "Scheme", ["Basic", "Bearer", "OAuthClientCredentials"]) with { IgnoreChoiceCase = true },
                Create("Username", "Username"), Create("Password", "Password", OptionEditorKind.Secret) with { AllowEmptyText = true }, Create("Token", "Token", OptionEditorKind.Secret),
                Address("TokenEndpoint", "Token endpoint", HttpAuthenticationConfiguration.ParseTokenEndpoint), Create("ClientId", "Client ID"), Create("ClientSecret", "Client secret", OptionEditorKind.Secret),
                Create("Scope", "Scope"), EnumSelect("ClientCredentialStyle", "Credential style", ClientCredentialStyle.AuthorizationHeader)])
                { IsVisible = AuthenticationFieldVisible },
            new("Retry", "Retry", [Create("MaxRetryAttempts", "Attempts", OptionEditorKind.Integer, "Zero disables retries.", 3) with { Min = 0, Max = int.MaxValue },
                Create("Delay", "Delay", OptionEditorKind.Duration, defaultValue: "00:00:02") with { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
                EnumSelect("BackoffType", "Backoff", DelayBackoffType.Exponential),
                Create("UseJitter", "Jitter", OptionEditorKind.Toggle, defaultValue: true), Create("MaxDelay", "Max delay", OptionEditorKind.Duration, defaultValue: "00:00:30") with { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
                Create("ShouldRetryAfterHeader", "Retry-After", OptionEditorKind.Toggle, defaultValue: true),
                Create("RetryUnsafeMethods", "Retry writes", OptionEditorKind.Toggle, defaultValue: false)]) { AddBeforeEditing = true }
        ],
        Validate = ValidateConnection
    };

    private static OptionEditorModel Address(string name, string label, Func<string?, Uri?> parse) =>
        Create(name, label) with { ValidateText = value =>
        {
            if (string.IsNullOrEmpty(value)) return null; // Allow removing an override or an inactive OAuth field.
            try { parse(value); return null; }
            catch (ArgumentException error) { return error.Message; }
        } };

    private static void ValidateConnection(IConfiguration settings)
    {
        try
        {
            var config = settings.Get<HttpClientConfiguration>() ?? throw new ArgumentException("Missing HTTP settings.");
            HttpConnectionSettings.Read(config);
            HttpConnectionSettings.Retry<RetryStrategyOptions<HttpResponseMessage>>(config.Retry);
        }
        catch (FormatException) { throw new ArgumentException("Check the format of the HTTP settings and default headers."); }
    }

    private static bool AuthenticationFieldVisible(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        if (name == "Scheme") return true;
        var saved = Field(values, name);
        if (saved.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) return true;
        var scheme = Field(values, "Scheme");
        if (scheme.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
        if (scheme.ValueKind != JsonValueKind.String) return true;
        return scheme.GetString()?.ToUpperInvariant() switch
        {
            "BASIC" => name is "Username" or "Password",
            "BEARER" => name == "Token",
            "OAUTHCLIENTCREDENTIALS" => name is "TokenEndpoint" or "ClientId" or "ClientSecret" or "Scope" or "ClientCredentialStyle",
            "" => false,
            _ => true
        };
    }

    private static string? ValidateHeader(string name, string value)
    {
        try { using var request = new HttpRequestMessage(); request.Headers.Add(name, value); return null; }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        { return "Enter a valid HTTP request header name and value."; }
    }
}
