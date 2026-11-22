namespace FluxFlow.Components.Bluetooth.Windows;

internal static class OwnedNativeOperation
{
    public static async Task RunAsync(Func<Task> operation, Func<Task> cleanup)
    {
        Exception? operationError = null;
        try { await operation(); }
        catch (Exception error) { operationError = error; throw; }
        finally
        {
            try { await cleanup(); }
            catch (Exception cleanupError) when (operationError is not null)
            {
                throw new AggregateException("Native operation and cleanup both failed.", operationError, cleanupError);
            }
        }
    }

    // Resource-producing Windows operations may complete after cancellation. Await ownership
    // before observing cancellation so a late device handle cannot escape disposal.
    public static async Task<T> AcquireAsync<T>(Func<Task<T>> create, CancellationToken cancellation)
        where T : class, IDisposable
    {
        cancellation.ThrowIfCancellationRequested();
        var resource = await create();
        if (!cancellation.IsCancellationRequested) return resource;
        resource?.Dispose();
        cancellation.ThrowIfCancellationRequested();
        return resource!;
    }
}
