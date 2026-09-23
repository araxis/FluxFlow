using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.RateLimit;

public sealed class RateLimitOptions
{
    /// <summary>Configured native .NET limiter. Caller owns its lifetime and may share it.</summary>
    public required RateLimiter Limiter { get; init; }
    /// <summary>Deadline after a lease is acquired, covering request admission and reply waiting.</summary>
    public TimeSpan ResponseTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    /// <summary>By default up to 128 accepted inputs can reach the limiter concurrently.</summary>
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
