namespace FluxFlow.Resilience;

public enum RetryDirectiveKind
{
    Attempt = 0,
    Wait = 1,
    Complete = 2,
    Exhausted = 3
}

public enum RetryStateStatus
{
    Active = 0,
    Waiting = 1,
    Completed = 2,
    Exhausted = 3
}

public readonly record struct RetryState(
    DateTimeOffset StartedAt,
    int Attempt,
    RetryStateStatus Status);

public readonly record struct RetryDirective
{
    private RetryDirective(RetryDirectiveKind kind, RetryState state, TimeSpan delay)
    {
        Kind = kind;
        State = state;
        Delay = delay;
    }

    public RetryDirectiveKind Kind { get; }

    public RetryState State { get; }

    public int Attempt => State.Attempt;

    public TimeSpan Delay { get; }

    internal static RetryDirective AttemptNow(int attempt, DateTimeOffset startedAt)
        => new(RetryDirectiveKind.Attempt, new RetryState(startedAt, attempt, RetryStateStatus.Active), TimeSpan.Zero);

    internal static RetryDirective Wait(int attempt, DateTimeOffset startedAt, TimeSpan delay)
        => new(RetryDirectiveKind.Wait, new RetryState(startedAt, attempt, RetryStateStatus.Waiting), delay);

    internal static RetryDirective Complete(RetryState state)
        => new(RetryDirectiveKind.Complete, state with { Status = RetryStateStatus.Completed }, TimeSpan.Zero);

    internal static RetryDirective Exhausted(int attempt, DateTimeOffset startedAt)
        => new(RetryDirectiveKind.Exhausted, new RetryState(startedAt, attempt, RetryStateStatus.Exhausted), TimeSpan.Zero);
}

public sealed class RetryStateMachine
{
    private readonly RetryPolicy _policy;

    public RetryStateMachine(RetryPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public RetryDirective Begin(DateTimeOffset now)
        => RetryDirective.AttemptNow(attempt: 1, now);

    public RetryDirective AfterFailure(
        RetryState state,
        DateTimeOffset now,
        double jitterSample = 0.5)
    {
        return state.Status switch
        {
            RetryStateStatus.Completed => RetryDirective.Complete(state),
            RetryStateStatus.Exhausted => RetryDirective.Exhausted(state.Attempt, state.StartedAt),
            RetryStateStatus.Waiting => throw new InvalidOperationException("A retry delay must finish before another failure is applied."),
            RetryStateStatus.Active => RetryPlanner.PlanAttempt(
                _policy,
                checked(state.Attempt + 1),
                state.StartedAt,
                now,
                jitterSample),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state.Status, "Retry state is not supported.")
        };
    }

    public RetryDirective AfterDelay(RetryState state, DateTimeOffset now)
    {
        if (state.Status != RetryStateStatus.Waiting)
            throw new InvalidOperationException("Only a waiting retry state can begin its next attempt.");
        if (_policy.MaximumDuration is { } maximumDuration && now - state.StartedAt >= maximumDuration)
            return RetryDirective.Exhausted(state.Attempt, state.StartedAt);
        return RetryDirective.AttemptNow(state.Attempt, state.StartedAt);
    }

    public RetryDirective Complete(RetryState state)
        => RetryDirective.Complete(state);
}
