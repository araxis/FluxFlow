using Microsoft.Extensions.Hosting;

namespace FluxFlow.Studio.Desktop;

internal static class DesktopHostLifetime
{
    public static async Task StopAsync(IHost host)
    {
        List<Exception> errors = [];
        try { await host.StopAsync(); }
        catch (Exception error) { errors.Add(error); }
        try
        {
            if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync();
            else host.Dispose();
        }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Studio host cleanup failed.", errors);
    }
}
