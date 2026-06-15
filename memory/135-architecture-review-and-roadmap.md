# Architecture Review and 2.0 Roadmap

Date: 2026-06-15

Deep per-component review (engine + 27 packages) against four owner-stated
architecture principles, run as a 9-reviewer workflow with synthesis and a
completeness critic. Full evidence had file:line citations; the load-bearing
claims were spot-verified against source.

## Principle verdicts

1. **No config/registration/JSON in node logic** — node *classes* are clean
   almost everywhere (typed-record ctors; no `JsonElement`/`NodeDefinition`/
   `ResolveType` in message paths). The smell is the co-located
   `static Create(RuntimeNodeFactoryContext …)` living in node files. Three
   real violations: `JsonSchemaValidatorNode` (does `File.ReadAllText` +
   `JsonSchema.FromText` inside `StartAsync`, exposes raw options to selectors
   at message time — worst offender); `StateReducerNode.Create`; and
   `TimerIntervalNode`/`TimerScheduleNode` embed the factory while sibling
   timers use dedicated `*NodeFactory` classes.

2. **String conditions vs compiled delegates** — VINDICATED, the epicenter.
   `IFlowExpressionEngine` has no `Compile` step; 10 nodes store a raw
   expression string + engine and `Evaluate` per message: `flow.filter`,
   `flow.when`, `flow.mapper`, `flow.assert`, `flow.switch`,
   `flow.correlation`, `flow.join`, `state.reducer`, `flow.counter`, and the
   engine's own link `when` (`ApplicationRuntimeBuilder.cs:271` →
   `OutputPort.cs:326`). The correct primitive (`DelegateFlowPredicate`/
   `DelegateFlowMapper`) exists but is unused on the hot path. Target: add a
   compile API, compile once in the factory, inject the delegate; node never
   sees the string or the engine. Bonus: type errors fail at build, not as
   per-message `InvalidCastException`.

3. **Connections as first-class components** — VINDICATED. No
   `mqtt.connection`/`http.client`/`storage.store`; connection profile +
   reconnect inlined per operation node (reconnect-ownership defect; N shared
   nodes spin N health monitors over one stream; HTTP gives every request node
   its own `HttpClient`+pool). The engine already has the `$resources`
   mechanism (`RuntimeNodeFactoryContext.GetResource`, `$resources` scope),
   unused by IO packages. The existing `Resources`/`Secrets`/`Configuration`
   packages are design-time-only contracts not wired to the runtime
   (Resources doesn't reference the engine), so they don't provide this today.
   Journal's `IJournalStore` is the same shared-store shape (future LENS-3).

4. **Consistent fanout outputs incl. errors** — the literal "broadcast
   everywhere" rule is WRONG: `BroadcastBlock` is lossy (latest-only), so it
   would drop data and errors. Correct rule: node-exposed sources stay
   non-lossy single-consumer; the engine's `OutputPort<T>` already provides
   lossless multi-link fanout for data; errors/diagnostics use the non-lossy
   `FlowFanoutSource`. The real inconsistency to fix: **events** use lossy
   `BroadcastBlock` (timer/MQTT/file.watch) and `FlowEventCollector`
   double-broadcasts them — a dropped file-change or connection-health event
   is a correctness defect, not telemetry. Fix = route `Events` through
   `FlowFanoutSource`. Also: two near-duplicate fanout pumps (`OutputPort` +
   `FlowFanoutSource`) should be consolidated; correction from critic — not
   every output is `OutputPort`-wrapped (Diagnostics and some Errors are
   collected out-of-band, never exposed as wireable ports).

## Other notable issues

- HIGH: SqlFile `DisposeAsync` `ClearPool` evicts the shared pool for sibling
  nodes on the same file; `FlowJoinNode`/`FlowWindowNode` report-then-rethrow
  (tears the node down on a recoverable per-message expression error, while
  `FlowCorrelation` swallows — inconsistent).
- MEDIUM: HTTP auto-redirect bypasses the `AllowedHosts`/origin allow-list
  (SSRF); `metrics.aggregate` drops snapshots via `Post` on a full output;
  `flow.switch` serially awaits all output ports (one stalled consumer freezes
  the whole switch); timer CTS use-after-dispose race in correlation/join/
  window; correlation duplicate-side emits an error (should be a warning) and
  keeps stale `ReceivedAt`; `MqttSubscribeNode` completed-before-start race;
  `flow.mapper` silently drops input on expression failure (no `Failed` port).
- LOW: `RegisterType`/`ResolveType` lock asymmetry; `CronSchedule` O(scan)
  worst case; leaking standalone `Input/Output` extension overloads; dead
  clock-defaulting ctors; misc design-metadata drift.

## Fix plan (waves)

Legend: [A] additive/minor · [B] breaking → 2.0

- **Wave 0 — correctness, ship now [A]:** join/window rethrow → report-and-
  swallow; HTTP redirect allow-list re-validation; metrics back-pressure;
  timer CTS interlocked swap; correlation duplicate-side → warning; MQTT
  subscribe completed-before-start guard.
- **Wave 1 — additive engine seams [A]:** add `IFlowExpressionEngine.Compile*`;
  consolidate the two fanout pumps; route `Events` through non-lossy fanout +
  register as real ports; shared `ComponentTypeRegistry` + drop `Type.GetType`
  fallback; `GetResource<T>()`; `flow.mapper` `Failed` port; design-metadata
  fixes; `FlowNodeBase` dispose of fanout pumps.
- **Wave 2 — 2.0, principles 1 & 2 [B]:** compile expressions in factories and
  strip engine+string from the 10 nodes; fix `JsonSchemaValidatorNode`;
  relocate every co-located `static Create` into dedicated factories.
- **Wave 3 — 2.0, principle 3 [B]:** `mqtt.connection`/`http.client`/
  `storage.store` as `$resources` nodes owning client+reconnect+health;
  operation nodes keep only a name reference; fold in `TimeProvider` clock
  consolidation (replaces the 15 bespoke `IXxxClock`) and the reconnect
  relocation; wire Resources/Secrets/Configuration to validation/design
  metadata for the new resource nodes.

## Sequencing

Wave 0 + Wave 1 as minors now; batch all breaking changes (Waves 2+3) into one
2.0, expression-compile (Wave 2) before connections (Wave 3) since the cleaner
node shape lands first and Wave 3 touches wire format + designer + build
pipeline at once.
