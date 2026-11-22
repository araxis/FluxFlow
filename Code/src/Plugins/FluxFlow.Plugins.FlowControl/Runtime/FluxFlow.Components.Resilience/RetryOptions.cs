using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Polly.Retry;

namespace FluxFlow.Components.Resilience;

public sealed class RetryOptions
{
    /// <summary>Maximum time for admission and reply waiting in each individual attempt.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Controls which failures are retried, retry count and delay; does not set the attempt deadline.</summary>
    public RetryStrategyOptions<FlowMessage> RetryPolicy { get; init; } = new()
    {
        ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.Error?.IsTransient == true)
    };
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
