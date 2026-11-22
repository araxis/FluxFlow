# Compiled application coverage

Inventory from the published Studio companion with all 12 packaged families installed in a temporary plugin directory. The identifiers and editor fields below come from the portable descriptions. Runtime-only options, graph-level resources, file dependencies and conditional wiring must also be checked before closing a row.

All 37 shipping components now appear in acceptance workflows and have packed C# mappings. Of these, 36 have successful local workflow runs and clean restarts through Studio and both EXEs. BLE GATT Server exchanges real data with the ESP32, but this Windows adapter leaves its advertising status at Started after Stop. Clean server shutdown and rerun on the same companion remain open.

Run `python Code/eng/test-studio-compiled-app.py <distribution>` on Windows x64 after publishing Studio. Use `--case <fixture>` to repeat one catalog fixture. Each named successful parity case compares two Studio runs, two isolated .NET 10 launches and two .NET 11 launches, and checks the unsaved graph and Undo/Redo. Startup failures are marked separately from successful I/O. Use `--case orders` for the inline and file-based Mapper comparison and the invalid-engine checks. The broker cases require a local Mosquitto executable or Docker with the indicated RabbitMQ and NATS images.

The [Windows release check](../../docs/studio-release-check.md) runs `inventory` and `orders` against its fresh Studio publication and retains a separate log for each.

The file-based `orders` workflow also passed a two-PC deployment check. Studio on MAF-PC published both Windows x64 targets from the same unsaved workflow. MafDel received only the two EXEs and verified their SHA-256 hashes. With Studio closed, each EXE ran twice from its own folder using an isolated runtime without an SDK (.NET 10.0.11 or 11.0.0). All four runs matched both Studio runs: two Information records in `Unsaved.Orders`, first B-23 at 2950 cents, then C-42 at 1199 cents. Each exited with code 0 after graceful Stop.

The broker fixtures run on loopback. `amqp-feedback` uses `rabbitmq:4.1.0-management`; `nats-echo` and `jetstream-redelivery` use `nats:2.11.8-alpine` with JetStream enabled. `mqtt-manual-ack` starts a private Mosquitto process. These fixtures use disposable brokers and plugin stores.

`mqtt-manual-ack-tls` repeats the manual ACK workflow over mutual TLS with a temporary CA, encrypted client key and destination certificate paths. It requires OpenSSL (on PATH or bundled with Git for Windows). The EXEs run after the Studio client certificate and key have been removed. The collector case uses an authenticated local OTLP/HTTP endpoint. nats-echo-tls repeats the NATS workflow with a broker token and mutual TLS using environment-variable references. The client certificate, key and CA are relocated between Studio and the EXEs.

The standalone checks use the .NET 11.0.100 SDK, Windows x64, and an isolated .NET 10.0.15 runtime. The packaged CLI install/restore comparison also runs with SDK 10.0.404. Controlled component tests and live executable cases are listed separately so that a startup diagnostic cannot be mistaken for a device round trip.

### Device evidence

| Family | Controlled runtime checks on .NET 10 and 11 | Published Studio and EXEs |
|---|---|---|
| Serial | 70 tests per target, including framing, timed-out writes on a shared port and rerun. | Absent-port startup plus physical loopback on a dedicated USB ESP32. Raw writer and fixed-length reader returned 00 FF 0D 0A and 80 01 FE 7F, including a frame split across two writes. Serial Text echoed an empty line and Unicode text over CRLF. Each physical test passed twice per host. |
| Bluetooth | 25 tests per target, including exact bytes, late reads and GATT cleanup. | Client write/read and both Notify and Indicate delivered 00 FF to the Windows companion and EXEs on the first and second runs. The GATT Server central checked the initial value and read back its own write. Windows did not confirm advertising stopped on this adapter. Published validation checks also cover invalid operations, subscription timeouts and duplicate UUIDs. |
| ROS 2 | 32 tests per target. Both configuration and fluent registration relayed Unicode strings and disposed subscriptions on restart using a controlled RCL context. | Native Humble loopback and startup without a sourced distribution, each checked across restart. Both valid messages reached the subscription and the malformed message reached the error link. |

Run the corresponding project under `Code/tests/Unit/FluxFlow.Components.<family>.Tests` with `dotnet test -f net10.0` and `dotnet test -f net11.0`. Use the fixture names in the component rows for independent published checks.

### Reproduce the native Serial loopback

Use one dedicated port connected to a peer that echoes every byte at 115200 baud, 8-N-1. From the Code directory, run the following with the actual port. Both trials generate the graph in Studio and launch each published executable twice. The frame trial splits a four-byte frame across two writes and checks the exact received bytes. The text trial sends an empty line and a Unicode line over CRLF. A full catalog run skips these two fixtures unless the port is specified.

```powershell
python eng/test-studio-compiled-app.py $distribution --case serial-frame-echo --serial-echo-port $port
python eng/test-studio-compiled-app.py $distribution --case serial-text-echo --serial-echo-port $port
```

### Reproduce the over-the-air Bluetooth checks

The Windows radio must support the central and peripheral roles. Use the Windows Bluetooth companion for these trials. From Code, publish it into the disposable Studio distribution using the first command below. Use one dedicated BLE board running MicroPython. The two peer scripts under `eng/compiled-app-test/peers` use trial-specific service UUIDs. Start `ble_peripheral.py` and use the public address it prints as `$peer`. After the first two trials, replace the running peer script with `ble_central.py` (it needs the MicroPython `aioble` package). The central scans for the test server, checks its initial value, writes two bytes, reads them back and signals success with a second write. Each host gets a fresh server on restart.

```powershell
dotnet publish src/Studio/FluxFlow.StudioWebHost -c Release -f net11.0-windows10.0.19041.0 -p:EnableWindowsBle=true -o $distribution
python eng/test-studio-compiled-app.py $distribution --case ble-peer-notify --ble-peer $peer
python eng/test-studio-compiled-app.py $distribution --case ble-peer-indicate --ble-peer $peer
python eng/test-studio-compiled-app.py $distribution --case ble-server-peer --ble-server-peer
```

Physical results on an ESP32-D0WD-V3 with a Qualcomm FastConnect 7800 Windows adapter:

| Workflow | Studio | .NET 10 EXE | .NET 11 EXE |
|---|---|---|---|
| Client + Notify | One 00 FF notification and one identical read-back on each of two runs. | Same, clean exit twice. | Same, clean exit twice. |
| Client + Indicate | One 00 FF indication and one identical read-back on each of two runs. | Same, clean exit twice. | Same, clean exit twice. |
| GATT Server | Remote initial read, write and read-back succeeded. Stop failed at the host deadline. | Two fresh process starts delivered both expected writes. Both exited with the native Stop error. | Same two records and native Stop error on both launches. |

The Windows API still reported `Started` ten seconds after `StopAdvertising`. The native companion proof reproduced this without a workflow. A separate direct WinRT process also reported Started for eleven seconds after StopAdvertising, with no FluxFlow code loaded. The strict acceptance case remains red on this adapter. Relaunching the process proves the next instance can exchange data, but cannot establish a clean Stop on the previous instance.

An independent ESP32 scan on the same adapter tracked a unique 128-bit service UUID. With Studio running, the ESP32 received 76 matching reports across five two-second scan windows before Stop. It received zero across 18 windows afterward while the companion stayed alive and returned HTTP 500 at its ten-second Stop deadline. A separate WinRT provider then advertised the same UUID. The ESP32 received 78 matching reports across five windows before Stop and zero across 18 windows afterward. Other BLE traffic was present in every window. The provider still reported `Started` at the final sample, about 38 seconds after Stop. This establishes that the service was no longer visible to this scanner during the observation period. Confirmed native shutdown and same-companion rerun remain open.

The second-host Studio trial on MafDel used commit `c4bec4d`. Windows reported that the radio was on and supported the BLE peripheral role. Companion PID 27040 stayed alive across both run requests for service `126ef799-bf33-427f-a65f-61fef9f62450`. The first run logged `Aborted` with Windows error `Success`; neither `Started` nor `Stopped` appeared. The next run was rejected because the previous Stop was unconfirmed. This host therefore reproduced the restart guard, but did not establish a successful advertising session or clean shutdown.

Four native probes on MafDel checked callbacks and polled the provider for 25 seconds after Start and 20 seconds after Stop. With both advertising flags enabled, the original and a fresh UUID each produced `Aborted` with error `Success`. With only one flag enabled, both variants stayed at `Created`. None reached `Stopped`. A separate foreground-thread probe on the Qualcomm PC kept a Windows dispatcher running during both calls. It received `Aborted` and still reported that state 20 seconds after Stop.

Microsoft's Bluetooth team [confirmed a Windows bug](https://learn.microsoft.com/en-us/answers/questions/578453/when-i-stop-the-advertising-on-my-bluetooth-le-ser) that leaves a provider reporting `Started` after advertising stops. That matches the earlier Qualcomm observation. The cause of the later `Aborted` results is undetermined. The remaining acceptance check requires a native `Started` → `Stopped` transition followed by a successful second Studio run in the same companion PID. The published EXEs must also exit cleanly on each launch.

### Reproduce the native ROS loopback

Use a Windows x64 shell with ROS 2 Humble, Fast DDS and the native std_msgs type support in its environment. The trial uses a unique topic for each invocation. The fixture sends an empty string, an explicit null and a string containing a newline, Greek text and an emoji. It checks for exactly two received messages and one conversion error in order on each run. The separate `ros-native-unavailable` case exercises startup without a ROS installation in the process environment.

A temporary RoboStack environment worked with rcl 5.3.13, rmw_fastrtps_cpp 6.2.11 and std_msgs 4.9.2. From the Code directory, with Pixi available and `$distribution` pointing to freshly published Studio, run:

```powershell
$rosTrial = Join-Path ([IO.Path]::GetTempPath()) ("ros-loopback-" + [guid]::NewGuid().ToString("N"))
$trialPython = (Get-Command python).Source
pixi init --channel https://prefix.dev/robostack-humble --channel conda-forge $rosTrial
$manifest = Join-Path $rosTrial "pixi.toml"
pixi add --manifest-path $manifest ros-humble-rcl ros-humble-std-msgs ros-humble-rmw-fastrtps-cpp ros-humble-rcl-interfaces ros-humble-ros-environment ros-humble-rosgraph-msgs
pixi run --manifest-path $manifest $trialPython eng/test-studio-compiled-app.py $distribution --case ros-string-loopback
```

### Mapper acceptance results

The order fixture uses the published Studio companion and the designer's in-memory graph. Its unsaved Logger category is `Unsaved.Orders`. Values are integer cents. The selected data and expression files are removed before launching the EXEs.

| Expression source | Studio, first run and rerun | .NET 10 EXE, first launch and restart | .NET 11 EXE, first launch and restart |
|---|---|---|---|
| Inline | B-23 = 2950, C-42 = 1199 | Same two records in order | Same two records in order |
| Relative file | B-23 = 2950, C-42 = 1199 | Same two records in order | Same two records in order |

The comparison also checks Information severity and the Logger category. `mapper-recovery` exercises the error-only link and an unsaved `HandleErrors` setting: all six runs emitted `Value 2`, `Handled invalid input`, `Value 4`. `MapperConfigurationTests` checks registered engine selection, invalid engines and message lineage. For both inline and relative-file expressions, Studio Run rejects the unregistered engine. Both publish targets return a diagnostic naming the Mapper field and no artifact.

## Bluetooth

Family resources: `ble.peripheral`, `ble.adapter`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `ble.client` | Connection, DeviceAddress, AddressType, ServiceId, CharacteristicId, UseInputOperation, Operation, WriteMode, Value, TimeoutSeconds, OutputCapacity | Input → Output, Events | ble-invalid-operation checks invalid input before radio access. ble-peer-notify and ble-peer-indicate verify a write with response and an exact 00 FF read-back while the same characteristic is subscribed. |
| `ble.notifications` | Connection, DeviceAddress, AddressType, ServiceId, CharacteristicId, SubscriptionMode, TimeoutSeconds, OutputCapacity | — → Output, Events | ble-subscription-validation checks an out-of-range subscription timeout before native access. ble-peer-notify and ble-peer-indicate each verified one 00 FF delivery while a second component wrote the same characteristic. Each fresh launch reestablished its subscription. |
| `ble.server` | ServiceId, Characteristics, Adapter, IsConnectable, IsDiscoverable, OutputCapacity | Input → Output, Events | ble-server-validation checks duplicate characteristic UUIDs within a service before publishing it. ble-server-peer verifies the initial 00 FF, a remote 80 01 write/read-back, then the acknowledgement 55 AA. The Windows adapter did not confirm Stopped; do not count this fixture as a clean rerun. Write during held notification delivery has controlled runtime coverage. |

## FlowControl

Family resources: `rateLimiter.tokenBucket`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `rateLimit` | Limiter, ResponseTimeout, OutputCapacity | Input, Reply → Output, Request, Events | shared-budget checks contention between two workflows, rejection, refill and a fresh budget on restart. |
| `requestReply` | MaxInFlight, BufferCapacity, ReplyTimeout, TimeoutBehavior | Input, ACK, NACK → Request, Output, Events | flow-replies checks ACK/NACK with the reply payload and request ID, two in-flight timeouts and one queued NotDispatched result under StopDispatch. |
| `retry` | AttemptTimeout, MaxRetryAttempts, Delay, BackoffType, UseJitter, MaxDelay, Engine, ShouldRetry, OutputCapacity | Input, Reply → Output, Request, Events | retry-predicate checks a relative-file predicate error followed by success on the next input. |

## Http

Family resources: `http.client`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `http.client` | Client, OutputCapacity | Input → Output, Events | http-request-reply uses an existing service bound to a different endpoint from the manual connection. It verifies bearer authentication, exactly one POST per input, and 503 and 200 outputs. A dropped response produces one transport error, one POST and a caller-visible 500. The following C request succeeds. http-shared-oauth holds the token response until one HTTP Client times out. The patient client makes exactly one authenticated API POST on the shared connection, with fresh credentials after restart. |
| `http.trigger` | Route, Methods, ResponseMode, AcknowledgementStatusCode, ReplyTimeout, MaxPendingRequests, ShutdownMode, MaxRequestBodyBytes, OutputCapacity | Response → Output, Events | http-request-reply verifies 202, caller-visible 503 and 200 replies, 504 timeout, slot reuse, capture of Logger records and clean listener restart. http-abandoned-slot holds the first downstream call, observes 503 for overflow, then disconnects that caller. The old reply is released only after the replacement is admitted, and the replacement receives its own response. |

## Logging

Family resources: `logging.otlp`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `logger` | Logger, Category, OutputCapacity | Input → Output, Events | Order and category checked in inline, file, data-generator-batch and reducer-state. collector-logging checks two authenticated exports from a mixed valid/invalid batch. The three receipt logs must match in Studio and both EXEs on each start, including their order, message, category and respective Information, Error and Warning levels. |

## Mqtt

Family resources: `mqtt.client`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `mqtt.publisher` | Connection, OutputCapacity | Input → Output, Events | mqtt-manual-ack publishes B-23 with QoS 1 and checks receipt. mqtt-lost-publisher-ack drops B's broker confirmation, observes one publish error, one broker receipt and one delivery of B; the reconnect publishes C successfully. |
| `mqtt.subscriber` | Connection, Topics, OutputCapacity, AcknowledgementMode, MaxPendingAcknowledgements | Input, ACK → Output, Events | mqtt-manual-ack verifies delivery of B-23 at QoS 1 and one broker-observed PUBACK per run. mqtt-stale-delivery checks the rejection event for the previous run's ID and exactly one broker PUBACK for the current ID. mqtt-lost-subscriber-ack cuts the first outbound PUBACK: the broker redelivers B with DUP and receives one complete PUBACK per run. mqtt-pending-limit ACKs A, leaves B pending and rejects C. Each host reports the original limit failure and starts again. |
| `mqtt.connection` | Connection, OutputCapacity | Input → Output, Events | mqtt-manual-ack connects a shared client on command with AutoConnect=false. mqtt-controller-reconnect drops a PUBACK and the first reconnect, then issues Disconnect during backoff. A subsequent probe fails while disconnected. Explicit Connect resumes publication of C, with one B and one C at the broker per run. |

## Nats

Family resources: `nats.connection`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `nats.publisher` | Connection, TimeoutSeconds, Capacity, MaxMessageBytes | Input → Output, Events | nats-echo checks publication of a binary payload. nats-publish-disconnect shows local enqueue of B even though its frame never reaches the broker, then one delivery of C after reconnect. |
| `nats.subscriber` | Connection, Subject, QueueGroup, TimeoutSeconds, Capacity, MaxMessageBytes | — → Output, Events | nats-echo receives the payload via a queue group. nats-subscriber-rejoin drops B before the group member reads it, then verifies one C after rejoin. |
| `nats.request` | Connection, TimeoutSeconds, Capacity, MaxMessageBytes | Input → Output, Events | nats-echo checks inbox delivery and recovers after a missing responder between two binary requests. nats-late-inbox waits for both requests before sending the expired reply and then the valid reply to two distinct inboxes. nats-request-disconnect drops the reply to B and verifies one error, one responder observation and the correct reply to C. |
| `nats.reply` | Connection, TimeoutSeconds, Capacity, MaxMessageBytes | Input → Output, Events | nats-echo checks the reply payload and request header. nats-late-inbox sends a late reply while the next request is pending. nats-reply-disconnect cuts the outgoing reply to B, verifies its local Output and the caller timeout, then checks the right inbox receives C. |
| `nats.jetstream.publisher` | Connection, Stream, CreateStream, StreamSubject, Storage, MaxStreamMessages, MaxStreamBytes, MaxStreamAgeSeconds, TimeoutSeconds, Capacity, MaxMessageBytes | Input → Output, Events | jetstream-redelivery verifies a storage ACK for ORDERS. jetstream-same-id resends one message ID, checks the duplicate flag and one consumed message. jetstream-lost-storage-ack suppresses the confirmation after storage and observes a publish error, a duplicate ACK on explicit same-ID resend and exactly one consumed delivery. |
| `nats.jetstream.consumer` | Connection, Stream, CreateStream, StreamSubject, Storage, MaxStreamMessages, MaxStreamBytes, MaxStreamAgeSeconds, TimeoutSeconds, Capacity, MaxMessageBytes, Consumer, MaxInFlight, CreateConsumer, FilterSubject, DeliverPolicy, AckWaitSeconds, MaxDeliver | Input → Output, Events | jetstream-redelivery checks a durable consumer at MaxInFlight=1 and delivery count 2. jetstream-stale-delivery completes the next message at MaxInFlight=1 after rejecting the previous run's token. jetstream-pull-recovery drops the connection before the first delivery reaches the consumer, then settles the second attempt and the next message after reconnect. |
| `nats.jetstream.settlement` | Consumer, Capacity | Input → Output, Events | jetstream-redelivery checks delayed NAK then ACK on delivery 2, and Progress followed by Terminate on a separate delivery. jetstream-stale-delivery uses the error link for a stale ACK and the successful Output for the valid replacement. jetstream-lost-settlement-ack drops a double-ACK confirmation and verifies an explicit retry of the same token releases the only slot. |

## RabbitMQ

Family resources: `amqp.connection`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `amqp.publisher` | Connection, TimeoutSeconds, Capacity, MaxMessageBytes, Topology | Input → Output, Events | amqp-partial-routing verifies a broker NACK when a queue capped through its own typed declaration rejects B, exactly one B reaches the audit queue, and C is confirmed on the recovered path. The fixture also redeclares typed exchange and nested binding arguments from both components. amqp-lost-confirm cuts the broker ACK after B is stored, checks one ambiguous publish error, verifies B was delivered exactly once and confirms C after recovery. The relay counted the two independent publishes on every run. |
| `amqp.consumer` | Connection, Queue, Prefetch, Capacity, MaxMessageBytes, RequeueOnNack, Topology | Input, ACK, NACK → Output, Events | amqp-feedback exercises ACK and NACK without requeue at prefetch 1. amqp-stale-delivery checks that a stale NACK yields the rejection event, the current delivery stays pending and C finishes. amqp-consumer-cancel deletes the queue while B is unacknowledged, checks the broker cancellation failure, then recreates the queue on restart. |
| `amqp.settlement` | Consumer, Capacity | Input → Output, Events | amqp-feedback ACKs C, rejects D, requeues E, then ACKs E on redelivery. The queue ends with zero ready and zero unacknowledged deliveries. amqp-stale-delivery also checks the explicit component's stale ACK error and successful ACK of the current delivery. |

## Ros2

Family resources: `ros.node`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `ros.publisher` | Node, Topic, Qos, OutputCapacity | Input → Output, Events | ros-native-unavailable verifies the missing distribution diagnostic on both starts. ros-string-loopback verifies native publication of empty and Unicode strings with an explicit null rejected between them. |
| `ros.subscriber` | Node, Topic, Qos, QueueSize, FullMode, IgnoreLocalPublications, OutputCapacity | — → Output, Events | ros-native-unavailable verifies the missing distribution diagnostic on both starts. ros-string-loopback checks both native deliveries and a fresh subscription at restart. |

## Routing

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `gate` | InitiallyOpen, OutputCapacity | Input, Open, Close → Output, Events | gate-controls: starts closed, transfer across workflows, Open/Close and skipped records. routing-keys: unsaved InitiallyOpen. |
| `router` | Engine, Routes, OutputCapacity | Input → Output, Unmatched, Events + named routes | routing-keys: file predicate, named output and Unmatched. router-overlap checks both matches, an error in the second predicate with zero partial deliveries, Unmatched and recovery on the next input. |
| `changeFilter` | Engine, KeySelector, IgnoreCase, OutputCapacity | Input → Output, Events | routing-keys and routing-expiry check consecutive mixed-case keys, selector errors, recovery and fresh state. |
| `deduplicate` | Engine, KeySelector, IgnoreCase, Retention, MaxKeys, OutputCapacity | Input → Output, Events | routing-keys: MaxKeys eviction and restart. routing-expiry checks a returning key after expiry, a selector error and recovery on the next key. |

## Serial

Family resources: `serial.port (workflow scope)`.

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `serial.text` | Connection, NewLine, MaxLineBytes, InputCapacity, OutputCapacity | Input → Output, Events | serial-missing-text verifies failure at startup for a confirmed absent port. serial-text-echo received the empty string and λ 😀 through a physical USB peer at 115200 baud. |
| `serial.reader` | Connection, Framing, DelimiterBase64, FrameLength, Encoding, MaxFrameBytes, ReadBufferBytes, Capacity, OutputFormat | — → Output, Events | serial-missing-frames verifies failure at startup for a confirmed absent port. serial-frame-echo joined two partial writes and checked both received frames byte-for-byte. Half-frame cancellation has controlled runtime coverage. |
| `serial.writer` | Connection, Framing, DelimiterBase64, FrameLength, Encoding, MaxFrameBytes, ReadBufferBytes, Capacity | Input → Output, Events | serial-missing-frames verifies the shared-port startup failure. serial-frame-echo exercised exact binary writes on a shared physical port. Partial-write timeouts have controlled runtime coverage. |

## Sources

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `scheduler` | Interval, At, Cron, IncludeSeconds, TimeZone, OutputCapacity | — → Output, Events | Interval (order totals), Cron with seconds and Europe/Stockholm (reducer-state). scheduler-stop-before-deadline checks past and future one-shot deadlines across restart and explicitly saved false IncludeSeconds for At and Interval. The exact race with Stop has controlled clock coverage. |
| `source` | Values, OutputCapacity | Input → Output, Events | Inline array (data-generator-batch), relative file (order totals). source-exhaustion checks silence after the last value and forwarding of an upstream error after exhaustion. |
| `dataGenerator` | Expression, Locale, OutputCapacity | Input → Output, Events | data-generator-batch: file expression, sv, fixed Bogus bounds. reducer-error-recovery checks an unsaved Swedish locale via weekday names and continues after an invalid sample. |

## Transformation

| Component type | Editor fields | Inputs → outputs | Verified cases or open work |
|---|---|---|---|
| `mapper` | Expression, HandleErrors, OutputCapacity | Input → Output, Events | inline/file: B-23 = 2950 and C-42 = 1199 cents; unknown engine rejected. mapper-recovery: error-only link and unsaved HandleErrors. Registered-engine lookup covered by MapperConfigurationTests. None of the 12 shipping families exports plugin-wide functions. Third-party function-only packages remain outside the verified compiled mappings. |
| `reducer` | Engine, InitialState, Reduce, AreEqual, OutputCapacity | Input → Output, Events | reducer-state: initial state file, reduce expression file, equality suppression, restart from 10. reducer-error-recovery verifies that generator, reduction and equality failures each produce one error and subsequent inputs continue from the last committed state. |
| `batch` | BatchSize, MaxWait, OutputCapacity | Input → Output, Events | data-generator-batch: size 2 then MaxWait flush of the remaining item. batch-stop verifies one ordered two-item flush during graceful Stop before the one-minute deadline. |

