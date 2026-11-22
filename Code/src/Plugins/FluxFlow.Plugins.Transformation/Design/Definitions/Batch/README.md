# Batch

Collects incoming messages into batches. Configuration type: `batch`.
Use it when the next operation should process a group rather than individual messages.

## Setup

Set **Batch size** to a positive count and **Maximum wait** to a positive duration,
such as `00:00:05`. The wait starts with the first collected message; later arrivals
do not reset it. A partial batch is emitted when the wait expires or on graceful completion.
`OutputCapacity` limits buffered batches and defaults to 128.

## Ports

**Input** receives individual messages; **Output** emits batches.
**Events** is the component event port. **Error** is Studio's filtered view of Output errors.
The batch value's JSON is an array of complete message envelopes, not just their values.
The outer message has its own identity; one reply to it does not answer every member's
request. Input errors bypass batching unchanged. Fault/cancellation discards unpublished work.
