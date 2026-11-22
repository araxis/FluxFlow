using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Hosting.AspNetCore;

public enum HttpTriggerResponseMode { Acknowledge, Reply }

/// <summary>Endpoint settings and links; server and middleware settings belong to the host.</summary>
public sealed record HttpTriggerConfiguration : OutputComponentConfiguration
{
    public static readonly TimeSpan MaximumReplyTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    public string Route { get; init; } = string.Empty;
    public string[] Methods { get; init; } = ["POST"];
    public HttpTriggerResponseMode ResponseMode { get; init; } = HttpTriggerResponseMode.Acknowledge;
    public HttpTriggerShutdownMode ShutdownMode { get; init; } = HttpTriggerShutdownMode.Cancel;
    public int AcknowledgementStatusCode { get; init; } = 202;
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxPendingRequests { get; init; } = 1024;
    public int OutputCapacity { get; init; } = 128;
    public int MaxRequestBodyBytes { get; init; } = 1_048_576;
    public OneOrManyPortReferences Response { get; init; } = OneOrManyPortReferences.Empty;

    /// <summary>Checks endpoint settings without creating a server. The host supplies its route parser.</summary>
    public static HttpTriggerConfiguration Read(IConfigurationSection section, Action<string>? validateRoute = null)
    {
        var config = section.ReadRequiredConfiguration<HttpTriggerConfiguration>();
        config.Validate(validateRoute);
        return config;
    }

    public void Validate(Action<string>? validateRoute = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Route);
        validateRoute?.Invoke(Route);
        if (Methods.Length == 0) throw new ArgumentException("At least one HTTP method is required.");
        foreach (var method in Methods) _ = new HttpMethod(method);
        if (!Enum.IsDefined(ResponseMode)) throw new ArgumentOutOfRangeException(nameof(ResponseMode));
        if (!Enum.IsDefined(ShutdownMode)) throw new ArgumentOutOfRangeException(nameof(ShutdownMode));
        if (AcknowledgementStatusCode is < 200 or > 299)
            throw new ArgumentOutOfRangeException(nameof(AcknowledgementStatusCode));
        if (ReplyTimeout <= TimeSpan.Zero || ReplyTimeout > MaximumReplyTimeout)
            throw new ArgumentOutOfRangeException(nameof(ReplyTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPendingRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRequestBodyBytes);
        if (ResponseMode == HttpTriggerResponseMode.Acknowledge && Response.Count != 0)
            throw new ArgumentException("Response links require ResponseMode Reply.");
    }
}
