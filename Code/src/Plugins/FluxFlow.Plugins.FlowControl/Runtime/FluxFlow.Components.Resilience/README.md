# FluxFlow.Components.Resilience

Retry is a four-port Dataflow component. Input receives the original message;
Request sends each attempt to an operation; Reply receives the operation's response;
Output publishes only the final result. There is no request delegate in options,
application dependency, address lookup or registration inside the component.

```csharp
await using var retry = new Retry(new RetryOptions
{
    AttemptTimeout = TimeSpan.FromSeconds(10),
    RetryPolicy = new Polly.Retry.RetryStrategyOptions<FlowMessage>
    {
        MaxRetryAttempts = 3, // retries after the initial attempt
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = Polly.DelayBackoffType.Exponential,
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.Error?.IsTransient == true)
    }
});
retry.Request.LinkTo(operation.Input);
operation.Output.LinkTo(retry.Reply);
retry.Output.LinkTo(next.Input);
await retry.Input.SendAsync(message);
```

Retry validates its options and builds its policy before allocating its owned
FlowRequestReply coordinator. Every attempt sends the
original payload with a fresh message and exchange correlation, retaining the
original trace, headers and causation. Operations must preserve request correlation
in their response (normally via request.WithValue). The first correlated response
wins. Late, duplicate and unrelated replies are consumed and ignored. A FlowError
reply resolves the attempt and is evaluated by the policy, not bypassed.

Final results restore the outer input's correlation, retain final reply headers,
and point causation to the final reply. Incoming errors on Input bypass unchanged.
On exhaustion the last FlowError is returned as data; final exceptions/timeouts use
the normal Core processing-error and original-exception diagnostics path.

Defaults: 30-second attempt deadline, three retries, two-second constant delay,
only FlowError.IsTransient results retried. RetryPolicy is native Polly configuration;
explicitly select TimeoutException or transport exceptions if they should retry.
Replacing RetryPolicy uses Polly's defaults, so specify ShouldHandle for error results.
Polly telemetry/OnRetry can observe intermediate attempts. Configure before construction.

Input processing is serial by default. Native Execution options control concurrency,
ordering and input capacity; each input has an independent retry budget. Request
and Output are bounded by OutputCapacity (128 by default), and Reply admission is
also bounded. Reply processing is independent of Input processing to avoid a
request/reply deadlock, even at maximum parallelism one. The coordinator has its
existing 1,024 pending-exchange limit.

Complete closes Input and drains accepted work while Reply remains available.
After processing and outputs drain, Reply completes automatically. Fault/execution
cancellation interrupts waits and delays. Dispose releases the owned coordinator.
Link Request to a consumer before sending Input. Request and Output are native
broadcasts: admission does not guarantee downstream delivery, and a slow consumer
can miss messages. Graceful completion waits for active request/reply attempts,
not for acknowledgment of final Output consumption. Fault is the abort path. Do not close Reply while
requests are outstanding. Composition owns all links and optional propagation.

The deadline covers admission and reply waiting. A timed-out request already
accepted by Request or downstream cannot be retracted; timeout does not undo an
external operation. Retrying writes requires idempotency, for example a stable
application key in headers. No Scheduler or Core behavior changes are introduced.
The module references Microsoft.Extensions.Resilience directly. Microsoft builds
this package on Polly, which remains a transitive dependency and supplies the
RetryStrategyOptions and pipeline APIs used here. No direct Polly package reference
is required by FluxFlow or its consumers. This package change does not replace the
underlying retry engine or establish a licensing exemption. Core remains independent
of either resilience package.

RetryPolicy reference: https://www.pollydocs.org/strategies/retry
