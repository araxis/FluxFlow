# Flow control

Rate Limit sends requests through a limiter owned by the execution host. Request–Reply uses the same runtime with its own concurrency limit and produces a correlated ACK, NACK, Timeout or NotDispatched result. Retry sends a fresh request for each attempt and matches replies to that attempt.

The local companion runs all three components. Add a **Rate limiter** connection and select it in Rate Limit's **Limiter** field. Its shared Property Grid offers burst size, refill count, refill interval and queue limit. The plugin registers a native token bucket through the existing resource API; no external service is needed.

Application resources share a budget within that application instance. Workflow resources have their own scope. A run owns and disposes its limiters; a new run gets a fresh budget. Existing custom-host limiter references remain supported by the component registrar.

Rate limiting controls dispatch frequency, not concurrent work. A reply does not return a token. Use Request–Reply when you need bounded concurrency. Set a finite response timeout when replies are not guaranteed: graceful Stop drains accepted work and continues waiting for outstanding replies under the existing timeout policy.
