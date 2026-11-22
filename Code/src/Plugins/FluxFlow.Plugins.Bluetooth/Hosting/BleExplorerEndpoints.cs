using FluxFlow.Components.Bluetooth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluxFlow.Plugins.Bluetooth;

internal sealed class BleExplorerEndpoints : FluxFlow.Execution.Plugins.IPluginEndpoints
{
    public void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/bluetooth/scan", (IBleExplorer explorer, CancellationToken cancellation) => Run(
            async token => new() { Devices = await explorer.ScanAsync(token) }, 15, cancellation));
        group.MapPost("/bluetooth/discover", (BleClientOptions options, IBleExplorer explorer, CancellationToken cancellation) => Run(
            async token => { options.ValidateDevice(); return new() { Services = await explorer.DiscoverAsync(options, token) }; }, options.TimeoutSeconds, cancellation))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
        group.MapPost("/bluetooth/read", (BleClientOptions options, IBleExplorer explorer, CancellationToken cancellation) => Run(
            async token => { options.Validate(); return new() { Value = await explorer.ReadAsync(options, token) }; }, options.TimeoutSeconds, cancellation))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
    }
    private static async Task<IResult> Run(Func<CancellationToken, Task<BleExploreResult>> action, int seconds, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        BleExploreResult result;
        try
        {
            if (seconds is < 1 or > 300) throw new ArgumentException("Timeout must be between 1 and 300 seconds.");
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            result = await action(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { result = new() { Error = "The Bluetooth operation timed out." }; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or PlatformNotSupportedException or TimeoutException)
        { result = new() { Error = error.Message }; }
        cancellation.ThrowIfCancellationRequested();
        return Results.Json(result, BleExplorerJsonContext.Default.BleExploreResult);
    }
}
