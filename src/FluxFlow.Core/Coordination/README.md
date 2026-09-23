# Component request/reply

`FlowRequestReply` supports components such as Retry and RateLimit that send work
through a Request port and receive its result through a Reply port. It does not
own transport connections, RabbitMQ exchanges, routing, retry policies or Application.

Each call creates a fresh correlation ID. Replies must match that correlation and
the expected reply name/address; a trace ID alone cannot distinguish concurrent
requests. The first matching reply completes the call. Duplicate, unrelated and
late replies return false. A previous attempt cannot complete a newer request.

The public API remains RequestAsync, Reply, PendingCount, Stop and DisposeAsync.
The deadline includes send admission and reply waiting. Expiry, cancellation and
Stop cancel and await cooperative send admission so it is not left detached.
An infinite timeout still occupies capacity and can be cancelled or stopped.
The original send/stop exception and caller cancellation token are preserved.

Internally, PendingRequests owns only active waiters and deadlines. One TimeProvider
timer serves all requests; elapsed deadlines are checked when replies arrive even
if the timer callback is delayed. Completion is atomic and continuations run
asynchronously. Deadline entries contain keys, never completed message payloads,
and stale entries are bounded by compaction. Timer failures release all waiters and
are reported during disposal; timer cleanup is attempted even when stopping fails.

The old public generic PendingExchangeCoordinator and its context, result, status,
options and settled-history models have been removed. There is no generic protocol
coordination API or distinction between late and duplicate feedback to learn.
