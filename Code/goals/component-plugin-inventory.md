# Component extraction baseline

Inventory captured before moving the Sources family. Component IDs below are case-sensitive. Every row includes Output and Events. Error links are filtered output links; preserve their original representation, including conditions and engine names. Ports before a semicolon are inputs, and additional outputs follow it. Runtime owners are the original Components projects unless indicated. Original descriptions are under Authoring/Definitions; configuration readers are under FluxFlow.Configuration/Components except the HTTP Trigger host integration.

| Persisted type | Original runtime | Package family | Other ports | Resource / editor scope | Baseline fixture or test source | Behavior test |
| --- | --- | --- | --- | --- | --- | --- |
| scheduler | Scheduler | Sources | — | Interval / Cron / At, time zone | NATS saved request/reply; S | SchedulerTests |
| source | Source | Sources | Input | Inline JSON array or runner file | S; SourceConfigurationTests | SourceTests |
| dataGenerator | DataGeneration | Sources | Input | Generator expression, scoped functions, locale | Hosting.Sample; S | DataGeneratorTests / BogusFunctionTests |
| logger | Logging | Logging | Input | Host logger or logging.otlp; category | NATS saved request/reply | LoggerTests / OtlpLoggerTests |
| gate | Routing | Routing | Input / Open / Close | Initially open | GateConfigurationTests | GateTests |
| router | Routing | Routing | Input; dynamic routes / Unmatched | Expression engine, named route collection | RouterConfigurationTests | RouterTests |
| changeFilter | ChangeFilter | Routing | Input | Engine and key selector | ChangeFilterTests | ChangeFilterTests |
| deduplicate | Deduplicate | Routing | Input | Engine, key, retention and limit | DeduplicateTests | DeduplicateTests |
| mapper | Mapping | Transformation | Input | Registered engine and shared expression editor | NATS saved request/reply | MapperTests |
| reducer | Reducer | Transformation | Input | Initial state, engine and two expressions | ReducerTests | ReducerTests |
| batch | Batch | Transformation | Input | Size and wait duration | BatchConfigurationTests | BatchTests |
| rateLimit | RateLimit | FlowControl | Input / Reply; Request | Host-owned limiter | RateLimitTests | RateLimitTests |
| requestReply | RateLimit | FlowControl | Input / ACK / NACK; Request | Private limiter, bounded coordination | RequestReplyConfigurationTests | RequestReplyTests / RequestReplyLifetimeTests |
| retry | Resilience | FlowControl | Input / Reply; Request | Attempt, delay and expression options | RetryTests | RetryTests |
| http.client | Http | HTTP | Input | http.client resource, auth and environment binding | Hosting.Http.Sample | HttpPostFailureTests / HttpClientRunTests |
| http.trigger | Http + Hosting.AspNetCore | HTTP | Response input; Output | ASP.NET Core routes and host authorization | Hosting.Http.Sample | HttpTriggerTests / HttpGatewayTests |
| ros.publisher | Ros2 | ROS 2 | Input | ros.node, typed host registration, topic/QoS | SharedStudioHostingTests.Ros | RosTopicTests |
| ros.subscriber | Ros2 | ROS 2 | — | ros.node, typed host registration, topic/QoS | SharedStudioHostingTests.Ros | RosTopicTests |
| ble.client | Bluetooth + Bluetooth.Windows | Bluetooth | Input | ble.peripheral / direct address, discovery and operation | BleClientEditorTests | BleOperationTests / BleTests |
| ble.notifications | Bluetooth + Bluetooth.Windows | Bluetooth | — | ble.peripheral / direct address, Subscribe mode and queue | BleTests | BleTests |
| ble.server | Bluetooth + Bluetooth.Windows | Bluetooth | Input | ble.adapter, advertising and Visual/JSON hierarchy | BleServerEditorTests | BleGattModelTests / BleTests |

S is the pre-extraction Sources trial under samples/Studio/Sources. When the baseline column names a test instead of a saved Studio file, it identifies the existing behavior to reproduce, not proof of a save/reopen trial. Freeze the actual project before extracting that family. For default ports and option details refer to the original component README and registration. All package lifecycle checks are pending at inventory time.

The four existing package owners are Serial, MQTT, RabbitMQ and NATS (including JetStream). WorkflowExecution currently registers the ordinary in-process components; HTTP Trigger requires an ASP.NET Core host and ROS publisher/subscriber registrations depend on host message types. BLE hardware needs the capable Windows companion, but its design must load without it. The sample-only readingReporter is registered by its host; it is not a shipped palette entry. The JSONata engine, shared JSON/file/expression editors, run capture and platform-registered resources are not additional palette components.

## Sources extraction

Pre-extraction input: `samples/Studio/Sources/scheduled-values.fluxflow` (SHA-256: 913aef660984ee395fa6b941cebd6c410f18136b745b5bba74f61dd023ca28a3). It retains an unknown field and non-default capacity and connects an interval clock, ordered sample values and a generator-local function using locale `sv`. The unmodified companion produced values 0, 42, 255 in order with generated values in [18, 25]. Two runs per framework stopped without additional output.

The `FluxFlow.Plugins.Sources` package physically owns the three original runtime projects (keeping their assembly names), registrations, settings, portable design and embedded help. The empty-store companion and browser do not reference these runtime implementations, Cronos or Bogus. Cron parsing and locale validation are supplied by the installed design. Source file references are validated offline without opening them.

Acceptance evidence from isolated stores on both .NET 8 and .NET 10:

- Installed the local NuGet artifact and restarted. Enumerated exactly the three exported types. Opened the saved fixture in a fresh browser; changed Source output capacity, removed a real canvas wire, and verified each edit, Undo and Redo through saved project JSON.
- Removed and restarted. The catalog was empty; the project could not run. Open/save retained the original complete document. Reinstalled, reopened, compared the whole project, and repeated the property and wire history checks.
- Ran twice after reinstall against the same fixture: ordered samples and generator bounds matched the baseline; Stop retained records and their count did not grow. No hardware or external service was accessed.
- Checked Scheduler, Source, Data Generation and configuration unit suites, the Studio property and document validation tests, the engine log transport/display contracts and the real RabbitMQ shutdown/disconnect regressions. Conventional source-only samples that use the registration extensions now explicitly reference the Sources package.
- Distribution-wide first-run provisioning and the cross-family matrix are still pending. The isolated package trial is not evidence of those distribution checks.

## Logging extraction

The original and packaged Scheduler → Source → Logger project is `samples/Studio/Logging/scheduled-log.fluxflow` (SHA-256: aed9ad5e122f49228193f6ae91bc6f04f0cd17f5c7504d642a67806b93e5eb94). On both .NET 8 and .NET 10 the original companion emitted a Warning in category `trial.sensor` with message `Sensor 42`. The second request produced a `node.processing_failed` output containing the original JSON deserialization error. The packaged fixture matched the warning through the OTLP display projection and preserved the complete error payload.

- Installed the local Sources and Logging packages into empty temporary stores. Verified the Logger and OTLP connection registrations, edited Logger capacity and a real wire, and exercised Undo/Redo. Ran twice before removal and twice after reinstall on each framework. Stop retained output without later additions.
- Removed only Logging and restarted: Sources still ran and the companion's own diagnostics remained available. The missing Logger and its incoming wire survived a browser save/reopen. After reinstall the whole saved project matched; property and wiring Undo/Redo still worked.
- OTLP resource registration and export behavior are covered by the native logging suite on both targets. The pre-extraction local companion did not support preparing a `logging.otlp` environment resource; the companion lifecycle trial therefore used the host logger. The plugin does not change that host limit.
- Studio tests cover visual and JSON OTLP settings, masked headers, numeric enum values, original key spelling, Cancel and history. Tests caught an open plugin JSON dialog left behind on selection change; the shared portable editor now cancels it on disposal. Log contract checks include original capture/transport/display and real RabbitMQ shutdown and unexpected disconnect.

## Routing and filtering extraction

Captured `samples/Studio/Routing/branching-values.fluxflow` before moving routing code (SHA-256: 06cf9c891e4b0158275f98e0f966926801c905aa5c70198f833bd30748af54ac). It exercises Gate, Change Filter, Deduplicate and Router with named Warm and Cold outputs, an Unmatched output, a conditional wire and an unknown saved field. On both unmodified .NET 8 and 10 companions, two runs each produced changed values [warm, cold, warm, other], unique values [warm, cold, other], and exactly one original value per destination. No new outputs arrived after Stop. These baseline results were recorded before the extraction.

The local `FluxFlow.Plugins.Routing.1.0.0-alpha.1.nupkg` owns all four registrations, the three runtime assemblies, portable design and help. Both target directories contain the family binaries; neither published bare companion contains them. The portable metadata identifies the expression map that contributes named outputs. The shared renderer and catalog consumer do not key off a Router type ID.

| Check | .NET 8 | .NET 10 |
| --- | --- | --- |
| Install Sources and Routing in an empty temporary store; enumerate exactly the four Routing nodes | Passed | Passed |
| Open the original project and find Warm, Cold and Unmatched on the Router | Passed | Passed |
| Edit Gate capacity; delete the conditional Warm wire in a roomier copy of the same graph; Undo/Redo both edits and compare full saved documents | Passed | Passed |
| Remove Routing, restart, retain Sources, reject running the incomplete graph, save and reopen without Routing | Passed | Passed |
| Reinstall, restore the complete document and repeat property/wiring edits | Passed | Passed |
| Run twice before removal and twice after reinstall; match changed, unique and branched values; retain counts after Stop | Passed | Passed |
| Set the Warm link condition to false in a temporary copy; receive no Warm delivery, with Cold and Unmatched unchanged | Passed | Passed |

The Studio regression `RoutingPluginTests.Transported_catalog_restores_named_ports_and_conditional_links_after_missing_plugin_save` covers the serialized catalog boundary. The component suites cover Gate controls, route matching, change detection and duplicate retention. Configuration tests, Studio editor and history checks, and capture/transport tests (including RabbitMQ shutdown and unexpected disconnects) were exercised on both frameworks.

A fresh package and two isolated browser trials reproduced the results above on .NET 8 and 10. The Studio regression also deletes a conditional named wire after reinstall, checks that its route definition remains, and compares the entire restored project after Undo before checking Redo. The focused Studio and runtime suites passed all 44 test executions across the two frameworks.

## Transformation and aggregation extraction

The pre-extraction project `samples/Studio/Transformation/running-total.fluxflow` (SHA-256: 08679cd895fef05fe09d392af1cb29aed3d2dd01392be1147101f152b787fde8) contains four JSON readings, an inline Mapper, a stateful Reducer, a Batch with a nondefault capacity, real wires and an unknown Mapper property. The three runtime assemblies, configuration readers, portable design and help ship in `FluxFlow.Plugins.Transformation.1.0.0-alpha.1.nupkg` for both targets. No family assembly was present in either bare companion publish directory.

| Trial from the installed package | .NET 8 | .NET 10 |
| --- | --- | --- |
| Match original Mapper output [2, 0, 3, 4] and expression preview; Reducer emits [2, 5, 9], suppresses the unchanged state and starts fresh on rerun | Passed | Passed |
| Count flush [2, 5] and timed partial flush [9]; compare nested MessageId, TraceId, CausationId, timestamp, headers and value to the observed Reducer messages | Passed | Passed |
| Stop with three pending items before the timer: a single batch [2, 5, 9], with the original IDs; repeat on a fresh run and on the pre-extraction companion | Passed | Passed |
| Install Sources and Transformation, change Batch capacity and delete the Reducer-to-Batch wire, Undo and Redo each edit and compare saved documents | Passed | Passed |
| Remove and restart, keep Sources available, reject incomplete run, save and reopen the missing nodes; reinstall and restore the identical full project, repeat edits and run twice | Passed | Passed |

`TransformationPluginTests` checks the transported catalog, the new-node JSONata preset and the editable engine on imported C# Mappers through missing-plugin save/reopen. `MapperConfigurationTests` exercises C# on an explicitly registered engine; installing Transformation does not install an engine. Mapper, Reducer, Batch and configuration suites passed on both targets, as did the Studio history, preview, diagnostics and headless inspection checks and the capture/transport checks for RabbitMQ shutdown and unexpected disconnect.

## Flow control baseline (before extraction)

`samples/Studio/FlowControl/coordinated-replies.fluxflow` sends four values through a single in-flight Request–Reply with two conditional request wires. On the original .NET 8 and 10 companions, two runs each produced ACK for "accept" with the successful Retry reply, NACK for "reject" with the rejection payload, Timeout for "silent", and NotDispatched for "queued". Only "accept" reached Retry's Request port, once. Each completed run retained its output count after Stop. The limiter resource remains host-owned; its standalone Rate Limit registration and the installed Flow Control package lifecycle still need verification.

## Flow control extraction

`FluxFlow.Plugins.FlowControl.1.0.0-alpha.1.nupkg` (SHA-256: 30bdbcecfb00e2d974d71441eafcce15e2c51bd5041ffcedeb4b4bbb68a9d3b4) owns the two runtime assemblies, all three registrations, portable settings, property definitions and help. The two runtime assemblies are absent from the bare companion publishes. Request–Reply still creates a privately owned limiter on the existing Rate Limit implementation. The public Rate Limit instead resolves its host's keyed limiter.

| Trial using a temporary store | .NET 8 | .NET 10 |
| --- | --- | --- |
| Install alongside Sources and Transformation, open original coordinated-replies project, change MaxInFlight and delete the Retry Request wire, Undo/Redo both and compare complete saved documents | Passed | Passed |
| Remove, restart, reject incomplete run, save while absent, reinstall, recover identical document and repeat history edits; retain the separate Rate Limit host reference and unknown fields through the same lifecycle | Passed | Passed |
| Run twice before removal and twice after reinstall: ACK, NACK, Timeout and NotDispatched match original request and reply payloads; no new outputs after Stop | Passed | Passed |
| Local companion reports the scoped host-limiter requirement; an independent host using the installed package acquires the actual supplied limiter, returns a reply, releases the lease and respects both application and workflow disposal boundaries | Passed | Passed |
| Clean install of MQTT, Flow Control and Transformation: send QoS 1 messages through a local Mosquitto broker to Request–Reply. ACK and NACK each produce one workflow result; only ACK returns a broker PUBACK. Stop and rerun yield the same counts | Passed | Passed |

The independent host in `eng/flow-control-plugin-consumer` references the plugin loader, not the family. The first .NET 8 trial exposed an incompatible RateLimiter assembly identity; selecting the matching framework major fixed the actual package rather than requiring a dependency in Studio. Run the consumer with a temporary installed store as its sole argument.

The component suites cover reversed and late feedback, bounded admission, retry counts, timeout, cancellation and graceful completion. Configuration and Studio suites, transport checks (including real RabbitMQ shutdown/disconnect), and MQTT/RabbitMQ feedback runs were exercised on both targets.

## HTTP baseline (before extraction)

`samples/Studio/Http/correlated-gateway.fluxflow` (SHA-256: 1e5c94e8d5f1313c074e701c9e4e55550b2efa15e79360227017ea58840020df) is based on the saved HTTP gateway acceptance project. It preserves the HTTP connection, trigger route and method, request and response links, mapper expressions, environment and an unknown client setting.

Before moving HTTP, the original ASP.NET Core gateway and a separate loopback downstream server ran the fixture on both .NET 8 and .NET 10. Four concurrent POSTs returned their own route, body and distinct message ID. A downstream 503 and body arrived intact. An outbound timeout returned 500, a trigger reply timeout returned 504, and the next request succeeded. Stop returned 503 to the pending caller and to a caller arriving while stopped; rerun returned 200. All ten expected downstream POSTs arrived exactly once. These are baseline results, not verification of an HTTP plugin.

Preserve three boundaries during extraction: the `http.client` component and resource are different registrations; the ASP.NET Core route mapper is explicitly invoked by the host; environment mocks must strip authentication and Authorization headers even though manual connection credentials and saved snapshots must survive unchanged. The trigger method list and HTTP connection's optional retry settings must remain visually editable after the package is installed.

## HTTP extraction

The HTTP client, host-mapped trigger, connection settings, documentation and portable designer definitions now live under `src/Plugins/FluxFlow.Plugins.Http`. The installer must track node IDs and connection IDs in separate namespaces: both registrations named `http.client` exist in the original saved project. The module registers the client and its connection; only the ASP.NET host explicitly registers and maps triggers.

| Check against the frozen correlated-gateway project | .NET 8 | .NET 10 |
| --- | --- | --- |
| Install HTTP and Transformation archives in an empty store; start an independently built ASP.NET host with no HTTP project reference. Resolve the package's route adapter, opt in, and run the original downstream and gateway endpoints | Passed | Passed |
| Four concurrent correlated replies, downstream 503 and body, outbound and inbound timeout distinction, pending and stopped 503, successful rerun, ten unique downstream POSTs | Passed | Passed |
| Real Studio: edit client output capacity and remove its output wire, Undo/Redo each and compare complete project files; remove HTTP without affecting Transformation, save and reopen the missing nodes, reinstall, restore the same configuration and repeat edits | Passed | Passed |
| Default companion refuses the trigger and explains the explicit host requirement | Passed | Passed |

The component and host tests cover HTTP request handling, body disconnects, OAuth, retries and endpoint shutdown. The HTTP component suite first exposed a missing Microsoft.Extensions.Http dependency for OAuth on .NET 8. Both targets passed after fixing the package dependencies. The Studio HTTP checks exercise the native connection rules, method collection and conditional fields through the portable Inspector.

The HTTP package owns the WireMock endpoint form, validation, default mappings and container initialization. Studio renders the portable form. The companion provides container startup, readiness and cleanup only when the host enables container support. The Studio tests cover original JSON, invalid mappings, Cancel and locked profile snapshots; these caught a Razor binding that passed a literal instead of the saved mapping.

On both .NET 8 and 10 the final package passed the missing-plugin save/reopen and reinstall browser trial. The native ASP.NET host ran the original gateway from reinstalled modules on both targets. In two companion runs per target, installed Sources, Flow Control and HTTP packages sent POSTs to a fresh WireMock container. Its request journal showed one 201, three attempts for the 503, and one 400. It retained the ordinary headers and never received the original service Authorization header. Stop removed each container; the second run left the first run's retained outputs unchanged. The full Studio suite passed 3,526 tests, the environment integration selection passed 214 (including RabbitMQ shutdown/disconnect capture), and the Release solution built with zero errors.

## ROS 2 baseline and runtime move

Before moving the ROS sources, both Studio targets saved and reopened `samples/Studio/Ros2/topic-relay.fluxflow` identically (SHA-256 `06098826bce429d7d06533265dae9d032bba8ac47d8497a4c0ff93550f081b4f`). The fixture has a named node connection, namespace, nondefault QoS and capacity, unknown fields and a subscriber-to-publisher wire. Check environment named both components and explained that a generated message registration and native ROS host are required. The plain companion did not start the graph.

On ROS 2 Jazzy in Ubuntu 24.04, the existing typed host relayed both a Unicode `std_msgs/String` and a `geometry_msgs/Twist` with nonzero linear and angular values to an independent Python ROS peer. Graceful Stop was confirmed on .NET 8 (self-contained) and .NET 10. Both used loopback discovery, unique topic names and no robot hardware. These are pre-plugin results. The managed runtime source now lives under `src/Plugins/FluxFlow.Plugins.Ros2/Runtime`; all 40 runtime test executions passed after the move. The installed package now owns both components, the node resource, the portable QoS editor and all runtime assemblies. Neither the bare companion nor the portable design requires a ROS installation.

| ROS 2 acceptance | .NET 8 | .NET 10 |
| --- | --- | --- |
| Install in an empty store; enumerate both nodes and the node resource; diagnose missing native host and generated message registration without starting | Passed | Passed |
| Edit capacity and delete a wire, Undo/Redo, remove, save/reopen missing, reinstall and repeat the edits without changing the saved project | Passed | Passed |
| Use a generated-message host with the local package: exchange Unicode String and nonzero Twist with an independent ROS 2 Jazzy peer | Passed | Passed |
| Load from the reinstalled Studio store, relay the restored project twice, then shut down the native host | Passed | Passed |

The native trials used isolated localhost discovery in Ubuntu 24.04. They exercised ROS middleware and real peer traffic, without robot hardware.

## Bluetooth LE migration baseline

Before moving the implementation, Studio on .NET 8 and 10 saved and reopened three projects. The two existing editor fixtures preserve legacy single-service settings, multi-service descriptors, mixed-case keys, noncanonical numeric strings, manual UUIDs, a missing peripheral reference and original binary values. The loopback project was saved with the pre-extraction editor from the existing controlled trial, including its six wires, an adapter reference, a peripheral reference and an unknown writer setting.

| Fixture under `samples/Studio/Bluetooth/` | SHA-256 |
| --- | --- |
| `gatt-loopback.fluxflow` | `d5ab18e8c977636ed6949eafb44d237d62d09d4177ac67cd52355d845418f31c` |
| `client-targets.fluxflow` | `6b541373242ae94abb67318b69ea9b4c0bd663083da2dd3d3f0ef6dd3e68c49a` |
| `gatt-hierarchy.fluxflow` | `4a44c17daa817ccf62093386f5ed63877265a4cc736f732303fcfd810c27ca50` |

`BluetoothRoundTripTests` now runs the saved loopback with its source dependencies explicitly installed. On both targets Notify/Write and Indicate/WriteWithoutResponse each ran twice: initial read 01, write and readback 00 FF 42, update and notification FE 00 80. Stale callbacks did not modify retained observations. The native lifecycle proof with `--no-hardware` reported zero failures and one explicit hardware limitation per target. There was no radio access or over-the-air Stop confirmation.

Runtime, Windows driver, shared GATT model and portable design now reside together under `src/Plugins/FluxFlow.Plugins.Bluetooth/`. A console host on each target loaded and inspected the three descriptions and their help, and re-saved both GATT definitions without changing their JSON or loading a UI, driver or execution assembly. The plugin now registers its workflow nodes, discovery endpoints, capability checks and environment adapters. The published bare companions contain no Bluetooth DLL; both framework directories in the local package contain the design, model and runtime, and a separate Windows driver directory.

The local package and Sources were installed in fresh stores using the normal installer. A controlled probe loaded into the installed module’s assembly context supplied only the radio and exercised the saved loopback over the local companion HTTP API. On .NET 8 and 10, both Notify/WithResponse and Indicate/WithoutResponse passed twice, including exact read, write, updated and notification bytes, stale callbacks, Stop retention, and no default-context Bluetooth assembly. These are controlled results, not confirmation of over-the-air GATT Stop. Both Windows-targeted test entry points also resolved the installed native implementation as the shared platform and explorer without invoking a radio operation.

On both companions, an isolated browser installed Sources and Bluetooth, saved all three fixtures, removed Bluetooth, restarted, and saved and reopened each document without it. Only Sources remained registered. Reinstall restored each complete document. Editing the writer timeout and deleting a real wire, then undoing and redoing each edit, reproduced the expected saved documents before removal and after reinstall. The controlled runtime then ran the actual browser-restored loopback twice per subscription mode from those reinstalled stores.

Connection editing now uses portable field definitions for addresses, address types, adapter selection and malformed-endpoint repair. Device selection, the device → service → characteristic tree, cancellation and stale-discovery handling reside in the portable design assembly. Studio renders each tree branch through a shared control. The shared picker and Visual/JSON hierarchy form now consume plugin definitions; the server, client and notification Inspector have no family-specific Razor, startup registration or project reference in Studio. A console referencing only the package’s portable design and model (plus host contracts) read both saved hierarchies, added a custom descriptor, kept its selected characteristic and exact 00 FF 42 bytes across Visual/JSON, and selected a discovered characteristic with a controlled explorer on both targets. Neither the UI nor the native driver was loaded.

The clean published browser and both bare companions contained no Bluetooth files. Browser trials with the final package exercised the service hierarchy and device picker, Cancel, save/reopen, removal, reinstall and property/wiring Undo/Redo on both companions. The reinstated saved loopback then passed controlled Notify/WithResponse and Indicate/WithoutResponse with Stop and rerun. Full Studio suite: 3,528 test executions passed across .NET 8 and .NET 10. Live adapter delivery and over-the-air Stop confirmation remain unverified.

## Final coverage

Results below apply to both .NET 8 and .NET 10. Each of the eight families passed install, removal, save/reopen while missing, and reinstall using its saved fixtures. Complete project comparisons included the original configuration and wiring. Property and link edits were undone and redone within the editing session; restarting Studio does not persist an Undo stack. The preceding family sections identify fixtures, test suites and host requirements.

| Persisted component | Result on both targets | Behavior checked |
| --- | --- | --- |
| `scheduler` | Verified | Repeated ticks on the saved schedule; no later output after Stop. |
| `source` | Verified | Emitted the stored sequence 0, 42, 255 in order. |
| `dataGenerator` | Verified | Used the configured locale and scoped function; output stayed within the specified bounds. |
| `logger` | Verified | Preserved the Warning, category, message and processing failure; removing the node did not disable host diagnostics. OTLP resource tests are separate from the host-logger trial. |
| `gate` | Verified | Open/Close control behavior and saved wiring; editing capacity was reversible. |
| `router` | Verified | Warm, Cold and Unmatched ports survived removal and reinstall. Disabling the Warm condition suppressed only that branch. |
| `changeFilter` | Verified | Retained the second warm after a cold value, yielding warm, cold, warm, other. |
| `deduplicate` | Verified | Suppressed the repeated warm, yielding warm, cold, other; component tests cover expiry and retention limits. |
| `mapper` | Verified | Preview matched the installed runtime; an imported C# mapping retained its engine. |
| `reducer` | Verified | Produced 2, 5, 9; suppressed unchanged state and started fresh on rerun. |
| `batch` | Verified | Count, timer and Stop flushes retained the original message IDs and metadata. |
| `rateLimit` | Verified | An independent host supplied the keyed limiter; reply and disposal released its lease. |
| `requestReply` | Verified | Correlated ACK, NACK, Timeout and NotDispatched with the original requests; broker feedback acknowledged only the successful delivery. |
| `retry` | Verified | Local downstream journal counted one successful POST, three attempts for 503, and one for 400 on each run. |
| `http.client` | Verified | Original downstream body/status, timeout and POST counts checked against a local server. Its connection resource and component share an ID but resolve independently. |
| `http.trigger` | Verified | Explicit ASP.NET host returned the right reply to four concurrent callers and handled timeout, Stop and rerun. |
| `ros.publisher` | Verified | Generated-message host published Unicode String and nonzero Twist to an independent ROS 2 Jazzy peer. |
| `ros.subscriber` | Verified | Received typed data using loopback discovery; restored project ran twice and stopped. No robot was required. |
| `ble.client` | Controlled only | Packaged runtime read 01, wrote and read back 00 FF 42; manual and discovered targets survived editing. |
| `ble.notifications` | Controlled only | Notify and Indicate delivered FE 00 80; late callbacks did not change retained output. |
| `ble.server` | Controlled only | Legacy and multi-service definitions, descriptors, writes, rollback and late requests were checked. An unconfirmed advertising Stop remains unconfirmed; no over-the-air run is claimed. |

The four previously migrated packages were included in the installed-catalog and browser loading checks. Each of their following registrations was verified on both targets for ownership, portable definition and help. This does not imply a new hardware trial:

| Package | Verified component IDs | Connection resource |
| --- | --- | --- |
| Serial | `serial.reader`, `serial.writer`, `serial.text` | `serial.port` |
| MQTT | `mqtt.connection`, `mqtt.publisher`, `mqtt.subscriber` | `mqtt.client` |
| RabbitMQ | `amqp.consumer`, `amqp.publisher`, `amqp.settlement` | `amqp.connection` |
| NATS | `nats.publisher`, `nats.subscriber`, `nats.request`, `nats.reply`, `nats.jetstream.publisher`, `nats.jetstream.consumer`, `nats.jetstream.settlement` | `nats.connection` |

**Distribution:** 12 local packages own 37 distinct palette entries and 9 connection resources. The five other resources are `logging.otlp`, `http.client`, `ros.node`, `ble.adapter` and `ble.peripheral`. The host-supplied limiter, typed ROS registrations and sample-only readingReporter are not extra palette entries. Bare host and browser outputs have no component-family implementation dependency. Folder, ZIP and local NuGet installs were exercised; nothing was published remotely.

On a fresh store the standard distribution installed Sources, Logging, Routing, Transformation and Flow Control as five ordinary packages (14 components). Both published browsers opened the designer. With two companions sharing a store, removal waited for restart; removing Routing left the other four packages available. After removing the last package the store stayed empty through two restarts. The installed cross-family trials covered Scheduler → Logger, Retry → HTTP Client and MQTT → Request–Reply. Their family sections report the actual outputs, failure cases and Stop/rerun checks.
