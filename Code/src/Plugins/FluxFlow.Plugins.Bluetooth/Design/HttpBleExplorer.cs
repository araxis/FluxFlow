using System.Net.Http.Json;
using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed class HttpBleExplorer(HttpClient client) : IBleExplorer
{
    public async Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation) => (await Send("scan", null, cancellation)).Devices;
    public async Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions device, CancellationToken cancellation) => (await Send("discover", device, cancellation)).Services;
    public async Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => (await Send("read", options, cancellation)).Value
        ?? throw new InvalidOperationException("The companion returned no characteristic value.");
    private async Task<BleExploreResult> Send(string operation, BleClientOptions? options, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/runs/bluetooth/" + operation);
        request.Headers.Add("X-FluxFlow-Run", "1");
        if (options is not null) request.Content = JsonContent.Create(options, BleExplorerJsonContext.Default.BleClientOptions);
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(BleExplorerJsonContext.Default.BleExploreResult, cancellation)
            ?? throw new InvalidOperationException("The companion returned an empty Bluetooth response.");
        cancellation.ThrowIfCancellationRequested();
        if (result.Error is not null) throw new InvalidOperationException(result.Error);
        return result;
    }
}
