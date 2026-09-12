namespace FluxFlow.Nodes;

/// <summary>Starts every selected delivery, then joins them and releases blocked siblings on failure.</summary>
internal static class FlowDeliveryBatch
{
    internal static Task SendAsync<TTarget, TState>(TTarget[] targets, TState state,
        Func<TTarget, TState, ValueTask> send, Action<TState, Exception> fault,
        Exception? selectionFailure = null)
    {
        if (selectionFailure is null && targets.Length <= 1)
        {
            return targets.Length == 0 ? Task.CompletedTask : send(targets[0], state).AsTask();
        }

        var deliveries = new Task[targets.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            // A synchronous adapter failure must not abandon earlier offers or
            // prevent later siblings from seeing the same item.
            try { deliveries[index] = send(targets[index], state).AsTask(); }
            catch (Exception error) { deliveries[index] = Task.FromException(error); }
        }

        // Observe only after every sibling has been offered the item.
        if (selectionFailure is not null) ReportFailure(state, selectionFailure, fault);
        for (var index = 0; index < deliveries.Length; index++)
            deliveries[index] = ObserveAsync(deliveries[index], state, fault);
        var completion = Task.WhenAll(deliveries);
        return selectionFailure is null ? completion : PreserveSelectionFailureAsync(completion, selectionFailure);
    }

    private static async Task PreserveSelectionFailureAsync(Task completion, Exception selectionFailure)
    {
        try { await completion.ConfigureAwait(false); }
        finally
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(selectionFailure).Throw();
        }
    }

    private static async Task ObserveAsync<TState>(Task delivery, TState state, Action<TState, Exception> fault)
    {
        try { await delivery.ConfigureAwait(false); }
        catch (Exception exception)
        {
            ReportFailure(state, exception is AggregateException aggregate ? aggregate.GetBaseException() : exception, fault);
            throw;
        }
    }

    private static void ReportFailure<TState>(TState state, Exception error, Action<TState, Exception> fault)
    {
        try { fault(state, error); }
        catch
        {
            // Cancellation callbacks may throw. Still join every started delivery
            // and preserve the initiating delivery/selection failure.
        }
    }
}
