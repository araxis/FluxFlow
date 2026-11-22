using System.Net.Http.Json;
using FluxFlow.Environments;
using FluxFlow.Environments.Telemetry;

namespace FluxFlow.DesignerApp.Services;

public sealed class HttpEnvironmentRunner(HttpClient client) : IEnvironmentRunner
{
    public async Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/runs/check")
            { Content = JsonContent.Create(value, EnvironmentJsonContext.Default.EnvironmentRunRequest) };
        request.Headers.Add("X-FluxFlow-Run", "1");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.EnvironmentCheckResult, cancellationToken)
            ?? throw new InvalidOperationException("The companion returned an empty environment check.");
    }

    public Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, id, JsonContent.Create(request, EnvironmentJsonContext.Default.EnvironmentRunRequest), cancellationToken);
    public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, id, null, cancellationToken);
    public async Task StopAsync(Guid id, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Delete, id, null);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    private async Task<EnvironmentRunStatus> SendAsync(HttpMethod method, Guid id, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = Create(method, id, content);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.EnvironmentRunStatus, cancellationToken)
            ?? throw new InvalidOperationException("The companion returned an empty run status.");
        if (status.Id != id) throw new InvalidOperationException("The companion returned another run's status.");
        var logs = await ReadTelemetryAsync(id, "logs", cancellationToken);
        var diagnostics = await ReadTelemetryAsync(id, "diagnostics", cancellationToken);
        var traces = await ReadTelemetryAsync(id, "traces", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return status with
        {
            Logs = OtlpExecutionView.Logs(logs.Content, logs.Dropped),
            Diagnostics = OtlpExecutionView.Logs(diagnostics.Content, diagnostics.Dropped),
            Traces = OtlpExecutionView.Traces(traces.Content, traces.Dropped)
        };
    }

    private async Task<(byte[] Content, long Dropped)> ReadTelemetryAsync(Guid id, string signal, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/runs/{id}/telemetry/{signal}");
        request.Headers.Add("X-FluxFlow-Run", "1");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/x-protobuf")
            throw new InvalidOperationException("The companion returned an unsupported telemetry format.");
        var dropped = long.Parse(response.Headers.GetValues("X-FluxFlow-Dropped").Single(), System.Globalization.CultureInfo.InvariantCulture);
        if (dropped < 0) throw new InvalidOperationException("The companion returned an invalid telemetry position.");
        return (await response.Content.ReadAsByteArrayAsync(cancellationToken), dropped);
    }
    private static HttpRequestMessage Create(HttpMethod method, Guid id, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, $"api/runs/{id}") { Content = content };
        request.Headers.Add("X-FluxFlow-Run", "1");
        return request;
    }
}
