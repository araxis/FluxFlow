# Goal prompt — finish compiled publishing for the existing Studio catalog

Take ownership of this stage from inventory through release evidence. A user should be able to edit an existing supported workflow in Studio, choose **File → Publish compiled application**, close Studio, and run the Windows x64 executable on .NET 10 or .NET 11 with the same observable behavior. Work through the catalog in batches. Continue after each successful family. Record a precise blocker and continue independent work if a host, dependency or protected change prevents a check.

## 1. Establish the baseline

- Read `Code/eng/compiled-app.md`. Trace the actual path from Studio's unsaved graph and file picker through the packaged `dotnet-fluxflow` CLI, the family mapping NuGets and the published EXE. Run the existing order-total trial first.
- Enumerate component types from the shipping plugin registrations and reconcile them with the portable descriptions, runtime registrations and packed mappings. Record one coverage row per component and track each supported mode or setting within that row. Include resources, connection scope, input files, conditional ports, dynamic port names and supported link shapes. Specify which layer owns each dependency.
- The inventory contains 37 components and 10 resource types across 12 families. Each has a family mapping in the current checkout. Every component also appears in an acceptance fixture. Use the evidence matrix to distinguish passing scenarios from unverified modes. Update the inventory if the checkout differs.
- On resuming, read `Code/eng/compiled-component-coverage.md` and the checked-in acceptance fixtures. Reuse passing cases and fill the specific gaps recorded there. A mapping or a previous Studio-only trial does not establish compiled parity.

From the repository root on Windows x64, use a fresh distribution directory. The order trial is the fast baseline. The last command runs the catalog fixtures, including plugin removal, reopen and reinstall in a disposable store.

```powershell
$distribution = Join-Path ([IO.Path]::GetTempPath()) ("fluxflow-stage-" + [guid]::NewGuid().ToString("N"))
./Code/eng/publish-studio.ps1 -Framework net11.0 -Output $distribution
if ($LASTEXITCODE) { throw "Studio publish failed." }
python Code/eng/test-studio-compiled-app.py $distribution --case orders
if ($LASTEXITCODE) { throw "Mapper baseline failed." }
python Code/eng/test-studio-compiled-app.py $distribution
if ($LASTEXITCODE) { throw "Catalog acceptance failed." }
```

Install the matching Windows x64 .NET and ASP.NET Core runtimes, the repository SDK, and the isolated broker prerequisites listed in the coverage matrix. A failed fixture can be rerun by its name with `--case`. Capture the results before cleaning the distribution.

Run the dedicated Serial, Bluetooth and ROS 2 commands from the [coverage matrix](../../Code/eng/compiled-component-coverage.md) separately. The default command skips hardware cases without an explicitly selected peer. Use the Windows Bluetooth companion for the GATT tests. For the outstanding GATT Server Stop check, retain the native status and both processes’ exit results. Treat a fresh process that exchanges data successfully as a separate observation from a clean shutdown on the same companion.

## 2. Implement and review in this order

| Batch | Families and existing components | Behavior to prove |
|---|---|---|
| Local data and logging | Sources: Scheduler, Source, Data Generator. Transformation: JSONata Mapper, Reducer, Batch. Logging: Logger. | Schedule modes and time zone, inline and relative files, deterministic generator cases and locale, expression errors, reducer state, ordered batch flush and original Logger records. |
| Routing | Gate, Router, Change Filter, Deduplicate. | Open/Close and error messages, overlapping predicates, named and unmatched outputs, selector errors, retention, eviction and fresh state after restart. |
| Flow control | Retry, Rate Limit, Request–Reply. | Attempt counts, shared token budget and queue bounds, correlated ACK/NACK, Timeout and NotDispatched, graceful Stop and rejection of stale feedback. |
| HTTP | HTTP Client, HTTP Trigger. | Local server, request/reply correlation, 503 and timeout, disconnect, pending-slot reuse and listener release on Stop. Exercise an existing shared connection. |
| Brokers | MQTT Publisher, Subscriber and Connection Controller. RabbitMQ Publisher, Consumer and Acknowledge. NATS Publisher, Subscriber, Request and Reply. JetStream Publisher, Consumer and Settlement. | Use isolated local brokers and unique names. Check publish/confirm semantics, manual feedback, redelivery, stale tokens, responder timeout, JetStream settlement and restart. Use stable IDs to count downstream effects separately from delivery attempts. |
| Devices and native hosts | Serial Reader, Writer and Text. BLE Client, Notifications and GATT Server. ROS 2 Publisher and Subscriber. | Verify exact serial bytes and framing, BLE reads/writes and subscription cleanup, and the supported ROS message type. Use controlled peers to test timeouts, disconnect and late callbacks. Record OS, native runtime and hardware requirements. Use physical devices only with explicit authorization for the particular test. |

Use the existing public runtime, endpoints and portable definitions. Keep fluent methods, mappings and runtime dependencies in the owning family. Inspect the present one-mapping-per-package convention before grouping components. Preserve existing published package identities and mappings. Extend the shared CLI contract only for a concrete requirement demonstrated by a workflow. Keep one generator and use the same path for Studio and direct CLI generation.

Handle shared prerequisites when the first family needs them. These include resource registration and scope, project connections, environment-resolved settings, multiple workflows, conditional links and plugin-supplied expression functions. Verify identity and sharing in the running process. Use existing credential references and resolution mechanisms. Keep credentials out of generated source, fixtures, logs and reports. If a configuration cannot be represented faithfully, return an actionable diagnostic before replacing an artifact and list that configuration as open.

Use the existing component behavior tests as the expected contracts. Include these failure and restart cases when their component reaches compiled publishing:

- **MQTT.** Publisher loses a QoS 1 PUBACK after broker receipt. Subscriber holds a manual ACK at the pending limit, disconnects, then receives a redelivery and rejects the old delivery ID. Connection Controller reconnects without duplicating subscriptions. Count deliveries at the broker and compare Output with Events.
- **RabbitMQ.** Publisher receives a NACK when one destination queue rejects a publish and another accepts it. Consumer receives cancellation while a delivery is outstanding. Exercise its ACK/NACK ports and the separate Acknowledge component's Ack, Reject and Requeue actions. Check channel ownership and stale tokens after restart.
- **NATS and JetStream.** For ordinary publish, subscribe, request and reply, exercise queue groups, lost connections, missing responders and late replies to the original inbox. For JetStream distinguish a publish acknowledgement from storage, resend the same message ID, and exercise Consumer and Settlement together at one in flight with ACK, delayed NAK, Progress and Terminate.
- **HTTP and Flow Control.** Include two workflows sharing one limiter, a retry predicate failure, and Request–Reply with two requests in flight and one queued when dispatch stops. For HTTP, distinguish an HTTP status from a transport error and count actual POST attempts. For reply-mode Trigger, free the only pending slot on disconnect, reject a late reply, and reopen the same listener on rerun.
- **Serial, BLE and ROS 2.** On controlled peers, test fragmented UTF-8/CRLF for Text, a half-frame for Reader and a partial write for Writer. For BLE, verify exact read/write bytes, contention between Notify and Indicate, and a GATT Server write while a notification is outstanding. For ROS 2, test String payloads, subscription cleanup and native availability explicitly.
- **Local processing.** Include Scheduler deadline versus Stop, Source exhaustion and malformed or missing files, deterministic generator locale, bad input followed by valid input through Mapper, Reducer state and equality, and Batch size versus deadline versus Stop. Count Gate control effects, overlapping Router outputs, consecutive Change Filter keys and Deduplicate eviction and expiry. Compare Logger Output with captured log records, including a mixed valid/invalid batch.

## 3. Apply the same review to every family

1. **Configuration and wiring.** Compare exported settings with the actual runtime registration. Exercise defaults and unsaved non-default values, invalid values, saved names and case rules, resource and port references, and a reopened project. Check optional, named, control, event and error-filtered links where supported. Older projects must retain their meaning.
2. **Generation and packaging.** Inspect the emitted method calls, links and pinned package references. Restore from an empty package cache using the published packages. Exercise missing and duplicate mappings, missing runtime packages, unknown settings and invalid links. Verify relative files resolve from the project/export, their required contents survive embedding, and path collisions or missing inputs produce clear errors. Run the EXE from a clean directory with Studio closed and original embedded files removed.
3. **Runtime parity.** Use a fixed input with independently calculated expected results. Run the same edited workflow through Studio and both target EXEs. Stop and start each again. Compare output payloads, ordering where guaranteed, error outcomes, correlation relationships and external effects. Compare original log severity, category, message and exception details where applicable. Fresh timestamps, trace IDs and delivery tokens should belong to the correct run. Verify .NET 10 in an isolated .NET 10 runtime. Use bounded waits, local fixtures and clean teardown.
4. **Studio safeguards.** Use a temporary project. Exercise Generate, Publish, Cancel, failure and retry. Preserve the last successful artifact on failure. Verify Undo restores the previous graph, Redo restores the exact unsaved export, and the saved project is unchanged until Save. Exercise removal, save/reopen with a missing plugin and reinstall for the affected family. Keep user tabs, installed plugins, saved credentials and workflows untouched.
5. **Code review.** Inspect the family, CLI and packaging diffs. Eliminate duplicate evaluators, hard-coded family switches, linked source files and unnecessary wrappers. Keep runtime packages free of Studio UI dependencies. Fix reproduced discrepancies at their owner, add a focused regression check and rerun both the failing case and the baseline. Preserve Core and WFENG throughout.

## 4. Close the stage with evidence

Maintain a compact matrix with the component and mode, fixture or command, Studio result, .NET 10 result, .NET 11 result, restart result, and any external prerequisite. Reuse shared fixtures across rows while keeping failures attributable to a specific component. Add one repeatable entry point using the existing release scripts. It should report failures by family, component and target, retain diagnostics, and clean up its temporary projects, services and processes.

Mark the stage **complete** when every shipping component and supported mode has an implementation and review result, the clean publish and restart checks pass for both targets, and all configuration, Undo/Redo and compatibility checks pass. Distinguish controlled-adapter evidence from physical or native verification. Keep any required but unverified behavior open. Deliver the matrix, exact reproduction commands, fixes and remaining blockers. Use that evidence to recommend the next stage.

For the final review, reconcile the matrix against a fresh inventory from the packaged companion. Report counts for verified, partially verified and blocked components, plus the names and reproduction steps for any outstanding cases. Rebuild the final packages before repeating the combined suite to catch regressions introduced by later families. Record the SDK/runtime versions, broker versions and which checks used a simulated peer. The handoff should let a second developer reproduce the result with a clean checkout and disposable fixtures.
