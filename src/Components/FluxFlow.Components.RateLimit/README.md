# FluxFlow.Components.RateLimit

RateLimit exposes Input, Request, Reply and Output. It acquires one native .NET
rate-limiter permit, sends a correlated request, awaits its reply, then releases
the lease. This mirrors ASP.NET Core's acquire/await-next/dispose lifecycle, not
its HTTP routing, middleware, status codes or policy registration APIs.

```csharp
using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
{
    PermitLimit = 4,
    QueueLimit = 16,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
});
await using var component = new RateLimit(new RateLimitOptions
{
    Limiter = limiter,
    ResponseTimeout = TimeSpan.FromSeconds(30)
});
component.Request.LinkTo(operation.Input);
operation.Output.LinkTo(component.Reply);
component.Output.LinkTo(next.Input);
```

Configure native FixedWindowRateLimiter, SlidingWindowRateLimiter,
TokenBucketRateLimiter or ConcurrencyLimiter with their native options, then supply
the instance as Limiter. No algorithm enum, type switch, factory registry or copied
limiter settings. Caller owns/disposes the limiter after components finish and can
share it to enforce a common budget. Configure time-based automatic replenishment
or replenish externally. Partitioned policies/setup/JSON are outside this step.

Concurrency permits are held through request admission and reply waiting, not just
SendAsync. Success and error replies both release them. Time-based permits replenish
according to native settings; disposing a lease does not refund spent tokens.
AcquireAsync honors native QueueLimit/QueueProcessingOrder. A rejected lease emits
FlowError code rate_limit_rejected (transient, category rate_limit), including
retryAfterSeconds in Details when the native lease provides that metadata. No
request is sent on rejection. Limiter exceptions follow normal processing errors.

Input defaults to bounded capacity 128, unbounded degree of parallelism (bounded
by accepted input capacity), ordered results. This lets the native limiter control
concurrency instead of accidentally serializing every operation. Explicit native
Execution options override that, including ordering; ordered Output may wait for
an earlier request even when a later rejection/result is ready. Request order is
not promised under parallel processing. Request/Output/Reply each have bounded
capacity OutputCapacity (default 128). The coordinator has its existing 1,024
pending-exchange limit. Dataflow input buffering is separate from the limiter queue.

ResponseTimeout defaults to infinite and starts after acquiring the lease, covering
request admission and reply waiting. Cancellation also cancels limiter queue waits.
A finite timeout releases the lease and emits an error; it cannot stop downstream
side effects. Thus timed-out/canceled work can still execute externally after the
permit is released. Choose a terminal reply that really represents operation end
when controlling concurrency. Late, duplicate and unrelated replies are ignored.
With no reply/deadline, graceful Complete waits; Fault/cancellation is the abort path.

Input errors bypass limiting. Operations preserve the sent request's correlation
(normally using WithValue). Output restores the outer caller's correlation and
retains final reply headers/causation. Complete closes Input but keeps Reply open
while operations drain. Fault/cancellation releases acquired leases. Disposal
releases the owned coordinator, not the supplied limiter. There is no retry here.

References:
- https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
- https://github.com/dotnet/aspnetcore/blob/main/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs
