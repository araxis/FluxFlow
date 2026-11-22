using FluxFlow.Environments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluxFlow.Execution.Serial;

internal sealed class SerialDiscoveryEndpoints : FluxFlow.Execution.Plugins.IPluginEndpoints
{
    public void Map(IEndpointRouteBuilder group) => group.MapPost("/serial/ports", async (ISerialPortDiscovery discovery, CancellationToken cancellation) =>
    {
        SerialPortList result;
        try { result = new(await discovery.ListAsync(cancellation)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { result = new([], error.Message); }
        cancellation.ThrowIfCancellationRequested();
        return Results.Json(result, SerialDiscoveryJson.Default.SerialPortList);
    });
}
