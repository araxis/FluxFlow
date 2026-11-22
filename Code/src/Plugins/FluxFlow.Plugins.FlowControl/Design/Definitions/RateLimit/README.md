# Rate Limit

Limits delivery of requests using a host-owned RateLimiter resource.
Configuration type: `rateLimit`.

## Ports and wiring

Send work to **Input**. Link **Request** to the operation's Input and link the
operation's response back to **Reply**. The final result leaves **Output**.
Preserve correlation through the operation so replies can be matched.
**Events** is the component event port; **Error** filters Output errors.

## Setup

Add a **Rate limiter** connection at application or workflow scope, then select it
in **Limiter**. Its Property Grid uses a token bucket:

- **Burst size**: maximum accumulated tokens; the initial budget for a new run.
- **Refill count**: tokens added per interval, capped at burst size.
- **Refill interval**: automatic refill period, at least one millisecond.
- **Queue limit**: waiting permit acquisitions, shared by users of this resource.
  Oldest acquisitions are served first. Zero rejects immediately if no token is available.

Each dispatch consumes one token. Replies do not return tokens. This controls rate,
not in-flight concurrency; use Request–Reply for the latter.

**Response timeout** bounds the wait for a correlated reply; `-00:00:00.001` means no
timeout. It starts after a token is acquired, excluding time in the limiter queue.
`OutputCapacity` defaults to 128. Queue rejection produces the existing
`rate_limit_rejected` Output error without dispatching a Request.

## Lifecycle and compatibility

Graceful Stop drains accepted work, including queued acquisitions and outstanding
replies. Use a finite response timeout if replies may never arrive; an infinite
timeout can keep Stop waiting. Cancellation closes outstanding exchanges and
queued acquisitions. The resource owner disposes the limiter, including its refill
timer. Rerun creates a new budget and does not accept replies from the previous run.

Application resources are shared within one application instance; workflow resources
belong to that workflow and cannot be selected by another workflow. Use an application
resource to share a budget between workflows. Existing `Limiter` strings, wiring and custom-host native
RateLimiter registrations remain supported. The local companion requires a configured
`rateLimiter.tokenBucket` resource. It is an in-process resource, not a Docker service.

```json
{
  "Resources": {
    "budget": {
      "Type": "rateLimiter.tokenBucket",
      "TokenLimit": 10,
      "TokensPerPeriod": 10,
      "ReplenishmentPeriod": "00:00:01",
      "QueueLimit": 128
    }
  },
  "Workflows": {
    "main": {
      "limit": { "Type": "rateLimit", "Limiter": "budget", "ResponseTimeout": "00:00:10" }
    }
  }
}
```
