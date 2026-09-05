using FluxFlow.Engine.DurableInput;

namespace FluxFlow.Engine.DurableInput.Tests;

internal sealed class DurableInputCompletionTestSource : IDurableInputCompletionSource
{
    private readonly TaskCompletionSource _continueSubscription =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool BlockSubscription { get; set; }

    public bool ReturnNullSubscription { get; set; }

    public bool ReturnNullCompletion { get; set; }

    public bool CompleteOnSubscribe { get; set; }

    public Exception? CompletionGetterException { get; set; }

    public Exception? SubscribeException { get; set; }

    public Exception? DisposeException { get; set; }

    public int SubscribeCalls { get; private set; }

    public List<DurableInputLease> Leases { get; } = [];

    public TaskCompletionSource Subscribed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DurableInputCompletionTestSubscription? Subscription { get; private set; }

    public async ValueTask<IDurableInputCompletionSubscription> SubscribeAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubscribeCalls++;
        Leases.Add(lease);
        Subscribed.TrySetResult();
        if (SubscribeException is not null)
            throw SubscribeException;
        if (BlockSubscription)
            await _continueSubscription.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (ReturnNullSubscription)
            return null!;

        Subscription = new DurableInputCompletionTestSubscription(
            ReturnNullCompletion,
            DisposeException,
            CompletionGetterException);
        if (CompleteOnSubscribe)
            Subscription.Complete();
        return Subscription;
    }

    public void ContinueSubscription() => _continueSubscription.TrySetResult();
}

internal sealed class DurableInputCompletionTestSubscription(
    bool returnNullCompletion,
    Exception? disposeException,
    Exception? completionGetterException) : IDurableInputCompletionSubscription
{
    private readonly TaskCompletionSource<DurableInputCompletionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<DurableInputCompletionResult> Completion
    {
        get
        {
            if (completionGetterException is not null)
                throw completionGetterException;
            return returnNullCompletion ? null! : _completion.Task;
        }
    }

    public int DisposeCalls { get; private set; }

    public void Complete() => _completion.TrySetResult(DurableInputCompletionResult.Completed);

    public void Fail(string description) =>
        _completion.TrySetResult(DurableInputCompletionResult.Failed(description));

    public void ReturnNull() => _completion.TrySetResult(null!);

    public void Fault(Exception exception) => _completion.TrySetException(exception);

    public void Cancel() => _completion.TrySetCanceled();

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return disposeException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(disposeException);
    }
}
