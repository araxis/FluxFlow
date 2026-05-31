namespace FluxFlow.Engine.Runtime;

internal static class RuntimeCleanup
{
    public static void TryDisposeLink(
        IDisposable link,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            link.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing a link.",
                exception));
        }
    }

    public static void TryDisposeOutput(
        OutputPort output,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            output.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing output '{output.Address}'.",
                exception));
        }
    }

    public static async ValueTask TryDisposeOutputAsync(
        OutputPort output,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing output '{output.Address}'.",
                exception));
        }
    }

    public static void TryDisposeNode(
        RuntimeNode node,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            RuntimeNodeDisposal.Dispose(node.Node);
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing node '{node.Address}'.",
                exception));
        }
    }

    public static async ValueTask TryDisposeNodeAsync(
        RuntimeNode node,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            await RuntimeNodeDisposal.DisposeAsync(node.Node).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing node '{node.Address}'.",
                exception));
        }
    }

    public static void TryDisposeDiagnostics(
        FlowDiagnosticCollector collector,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            collector.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing diagnostics collector.",
                exception));
        }
    }

    public static async ValueTask TryDisposeDiagnosticsAsync(
        FlowDiagnosticCollector collector,
        ICollection<Exception> errors,
        string owner)
    {
        try
        {
            await collector.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(new InvalidOperationException(
                $"{owner} failed while disposing diagnostics collector.",
                exception));
        }
    }

    public static void ThrowIfErrors(string message, IReadOnlyCollection<Exception> errors)
    {
        if (errors.Count > 0)
        {
            throw new AggregateException(message, errors);
        }
    }
}
