# Request–Reply

`requestReply` matches each request with its first correlated ACK or NACK. It uses the existing Rate Limit runtime with a private concurrency limiter; no limiter resource or broker is required.

## Ports

| Port | Direction | Purpose |
| --- | --- | --- |
| Input | Input | Work to coordinate. |
| Request | Output | Work carrying a fresh request correlation. Connect this to the processing path. |
| ACK | Input | Completes the matching request with an ACK outcome, regardless of payload type. |
| NACK | Input | Completes the matching request with a NACK outcome, regardless of payload type. |
| Output | Output | A RequestReplyResult, with the original outer correlation restored. Unexpected internal failures remain engine errors. |
| Events | Output | Standard component events. |

## Configuration

- **MaxInFlight** defaults to 1. Larger values permit concurrent requests and out-of-order completion.
- **BufferCapacity** defaults to 128 additional messages. Input admission is bounded by this value plus MaxInFlight. Upstream delivery and overflow behavior still depend on the source and its links.
- **ReplyTimeout** defaults to 30 seconds and must be positive and finite. It covers request admission and waiting for feedback, after a processing slot is obtained.
- **TimeoutBehavior** defaults to `StopDispatch`. The timed-out request produces a `Timeout` result. Further work produces `NotDispatched` without being dispatched; already active requests may still complete. Stop and rerun to reset. `Continue` releases the slot and allows further requests, but does not cancel timed-out downstream work.

## Output contract

`RequestReplyResult` contains `Outcome` (`ACK`, `NACK`, `Timeout`, or `NotDispatched`), `RequestId` (the original input message ID), `Request` (the original payload), `Reply` (the original feedback payload), and `Elapsed` (time after acquiring a processing slot). `Reply` is null for Timeout and NotDispatched; NotDispatched has zero elapsed time. A JSON-null feedback payload is still distinguished by its ACK/NACK outcome.

An error payload arriving at ACK remains data inside an ACK result. An error arriving at NACK remains data inside a NACK result. Neither result matches the Error connection, which filters actual engine errors from Output. Route application outcomes by `Outcome`; for example, a JSONata link condition can use `Outcome = 'NACK'`. The result keeps payload values unchanged and retains reply headers and causation, while restoring the original request's correlation and trace identity.

ACK/NACK are application feedback, not broker acknowledgement. Preserve message correlation through the processing path. Duplicate, unknown and late replies cannot complete another request. A branch must join its required outcomes before replying: the first feedback does not imply that every branch finished. Retry is a separate concern.

## Stop and draining

Graceful application Stop stops new production and lets accepted work drain. Request–Reply continues dispatching its accepted, queued messages within `MaxInFlight`, and keeps ACK/NACK feedback available while that work finishes. Each dispatched request can complete with ACK, NACK or Timeout under the existing timeout policy.

For example, with `MaxInFlight = 1`, one active request and five accepted messages in the queue, graceful Stop allows those five messages to run as slots become available. Pressing Stop does not itself turn them into `NotDispatched` results.

If a request times out with `TimeoutBehavior = StopDispatch`, further dispatch stops, including during draining. Each queued message then produces its own `NotDispatched` result with its original `RequestId` and payload. Five queued messages produce five results, not one summary. Already active requests may still finish.

If shutdown reaches its deadline or is forcibly cancelled, pending exchanges are cancelled and resources are cleaned up. Cancellation does not manufacture ACK, NACK, Timeout or NotDispatched results, and delivery of a final result for every accepted message is not guaranteed during forced shutdown. A request timeout does not cancel downstream work.

Studio's local runner gives each instance 10 seconds to stop. If only successful responses feed ACK, a downstream error leaves the request waiting. When its remaining reply wait fits inside the shutdown window, it can emit a Timeout and drain. If the host deadline expires first, Stop fails and cleanup continues. A new run can start after resources are released. To complete a failed exchange immediately, wire the downstream Error to Request–Reply's NACK. Broker settlement remains a separate workflow decision.

Each run owns a fresh limiter and correlation scope. Ordinary `rateLimit` configurations retain their existing output contract.
