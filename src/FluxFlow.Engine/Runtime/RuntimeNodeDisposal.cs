namespace FluxFlow.Engine.Runtime;

internal static class RuntimeNodeDisposal
{
    public static void Dispose(object instance)
    {
        switch (instance)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
        }
    }

    public static async ValueTask DisposeAsync(object instance)
    {
        if (instance is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
