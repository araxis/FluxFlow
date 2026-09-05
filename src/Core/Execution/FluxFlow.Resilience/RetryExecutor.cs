using System.Runtime.ExceptionServices;

namespace FluxFlow.Resilience;

public sealed class RetryExecutor
{
    private readonly RetryStateMachine _stateMachine;
    private readonly TimeProvider _timeProvider;
    private readonly IRetryJitterSource _jitterSource;

    public RetryExecutor(
        RetryPolicy policy,
        TimeProvider? timeProvider = null,
        IRetryJitterSource? jitterSource = null)
    {
        _stateMachine = new RetryStateMachine(policy);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _jitterSource = jitterSource ?? RandomRetryJitterSource.Shared;
    }

    public async ValueTask<T> ExecuteAsync<T>(
        Func<int, CancellationToken, ValueTask<T>> operation,
        Func<T, bool> shouldRetryResult,
        Func<Exception, bool>? shouldRetryException = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldRetryResult);

        var directive = _stateMachine.Begin(_timeProvider.GetUtcNow());
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T result;
            Exception? failure = null;
            try
            {
                result = await operation(directive.Attempt, cancellationToken).ConfigureAwait(false);
                if (!shouldRetryResult(result))
                    return result;
            }
            catch (Exception exception) when (exception is not OperationCanceledException &&
                                              shouldRetryException?.Invoke(exception) == true)
            {
                result = default!;
                failure = exception;
            }

            var next = _stateMachine.AfterFailure(
                directive.State,
                _timeProvider.GetUtcNow(),
                _jitterSource.NextSample());
            if (next.Kind == RetryDirectiveKind.Exhausted)
            {
                if (failure is not null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
                return result;
            }

            if (next.Delay > TimeSpan.Zero)
                await Task.Delay(next.Delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            directive = _stateMachine.AfterDelay(next.State, _timeProvider.GetUtcNow());
            if (directive.Kind == RetryDirectiveKind.Exhausted)
            {
                if (failure is not null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
                return result;
            }
        }
    }
}
