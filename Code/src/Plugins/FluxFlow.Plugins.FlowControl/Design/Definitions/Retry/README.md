# Retry

Retries transient request failures. Configuration type: `retry`.

## Ports and wiring

Send work to **Input**, link **Request** to the operation, and return its result to
**Reply**. **Output** emits the final result. Preserve correlation through the operation.
**Events** is the component event port; **Error** filters Output errors.

## Setup

**Maximum retries** counts retries after the first attempt (default 3).
**Attempt timeout** defaults to 30 seconds; `-00:00:00.001` disables it.
**Delay**, **Backoff type**, **Use jitter** and **Maximum delay** control retry timing.
The existing policy requires at least one retry. Delay and Maximum delay accept
zero through one day; clearing Maximum delay removes the cap. Attempt timeout
must be positive and at most 49.17:02:47.294, or the infinite value above.
The Property Grid validates these bounds before committing an edit. Invalid saved
values remain available for repair; opening the Inspector does not rewrite them.
An optional **Retry expression** uses the configured **Engine**; when omitted,
the component retries transient errors.

Retrying can repeat side effects. Choose operations that tolerate retries or carry an
appropriate idempotency key. `OutputCapacity` defaults to 128.
