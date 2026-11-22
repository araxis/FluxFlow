# MQTT and serial-to-HTTP telemetry

A configuration-driven console host using existing components:

```text
MQTT broker -> telemetry.receive (Subscriber)
            -> telemetry.request (JSONata Mapper)
            -> telemetry.send (HTTP Client) -> HTTP API

connection.Error / send.Error -> errorRequest (JSONata Mapper) -> log (Logger)
```

`telemetry.connection` manages the shared MQTT resource's lifecycle and reconnects.
The subscription, mapping, HTTP resource and links live in `appsettings.json`.
The host uses standard Generic Host registration and shutdown; it creates no broker
or HTTP server. The sample is under `samples / Hosting / FluxFlow.Hosting.Telemetry.Sample`
both on disk and in Visual Studio.

## Serial text variant

The same host can select `SerialTelemetry` from `appsettings.json`:

```text
SerialText -> JSONata Mapper -> HTTP Client -> HTTP API
                               send.Error -> errorRequest -> Logger
```

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Telemetry.Sample -f net10.0 -- `
    --WorkflowSection=SerialTelemetry `
    --SerialTelemetry:Workflows:telemetry:Resources:port:PortName=COM3 `
    --SerialTelemetry:Resources:api:BaseAddress=http://127.0.0.1:5090/
```

Use `-f net8.0` for .NET 8. Set the port name for your machine (for example,
`COM3` or `/dev/ttyUSB0`). Only the selected section is built; this variant does
not connect to MQTT. The default remains the existing `FluxFlow` MQTT section.

The workflow owns its serial resource, configured for 115200 baud, 8N1. Each UTF-8
line terminated by LF or CRLF is a temperature, such as `23.5`. JSONata converts
the line with `$number($)` and sends `POST /readings` with `DeviceId`, `Temperature`,
`Unit`, `MessageId` and `TraceId`. `DeviceId` is `serial-sensor` in this example;
change the expression for another device or line format. Property names are
preserved as written in the expression. The original message ID also appears in
the `X-Flow-Message-Id` HTTP header.

Malformed numeric readings and HTTP transport failures follow the existing
`send.Error` logging path; later readings continue normally. Mapping errors pass
through the HTTP component unchanged. HTTP error status codes remain ordinary
HTTP responses, not transport errors. Failure to open the serial port prevents
startup. Runtime read/write failures fault execution and request host shutdown
through the default Generic Host behavior.
These fatal failures use execution completion and host logging, rather than the
recoverable `send.Error` path. This sample does not automatically reconnect a failed
serial device.

After a successful stop, restarting the workflow with the existing `FlowWorkflow`
API creates a fresh serial session. A faulted run cannot be restarted this way;
correct the cause and run a new host/application instance. Readings are not durably
stored or replayed. Normal host shutdown stops serial reception and drains accepted
HTTP work, subject to the HTTP timeout and host shutdown deadline. The host also
supports the existing `--RunFor=00:00:10` option.

Automated tests load this section with simulated serial I/O and a local HTTP server
to check mapping, error logging, recovery, restart and drain. Native port registration
is checked without opening a port. Physical hardware and driver behavior remain
unverified; the command above is for a later hardware trial.

## Run

Start an MQTT broker on `127.0.0.1:1883` and an HTTP API on `127.0.0.1:5090` accepting
`POST /readings`. From the repository root:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Telemetry.Sample -f net10.0
```

Use `-f net8.0` for .NET 8. Configuration is copied beside the executable and loaded
from that directory. Standard configuration overrides select other endpoints:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Telemetry.Sample -f net10.0 -- `
    --FluxFlow:Resources:broker:Address=mqtt://127.0.0.1:1883 `
    --FluxFlow:Resources:api:BaseAddress=http://127.0.0.1:5090/
```

Give each running instance a unique `FluxFlow:Resources:broker:ClientId`.
For a bounded run, pass `--RunFor=00:00:10` after `--`. This optional host setting
requests normal shutdown through the Generic Host cancellation token after ten
seconds. Without it, the host runs until stopped.

With an MQTT publishing client, send UTF-8 text `23.5` to `sensors/sensor-7/temperature`.
For example, if the Mosquitto client tools are installed:

```powershell
mosquitto_pub -h 127.0.0.1 -p 1883 -q 1 -t sensors/sensor-7/temperature -m 23.5
```

The subscription uses `sensors/+/temperature` at QoS 1. The JSONata expression reads
the device ID from the topic and converts `Text` with `$number`; it does not evaluate
incoming data as an expression. The API receives JSON:

```json
{
  "deviceId": "sensor-7",
  "temperature": 23.5,
  "unit": "C",
  "messageId": "<message ID>",
  "traceId": "<trace ID>"
}
```

The same message ID is sent as `X-Flow-Message-Id`. This sample accepts numeric text,
not a JSON object payload. Change the mapping for another sensor format.

## Prepare a real-device trial

Fill in [appsettings.trial.template.json](appsettings.trial.template.json) in a local
copy. It uses the same components and links as the sample. The template is not
loaded automatically, and placeholders are literal text, not variable substitutions.

| Placeholder or setting | What to enter |
| --- | --- |
| `<MQTT_ADDRESS>` | Full broker address, such as `mqtt://broker:1883`, `mqtts://broker:8883`, or `wss://broker/mqtt`. The scheme selects transport and TLS. |
| `<UNIQUE_CLIENT_ID>` | A client ID unique to this running instance, such as `plant-monitor-trial`. |
| `<MQTT_TOPIC>` | The device's exact topic for this first trial, such as `sensors/plant-1/temperature`. |
| `QoS` | Keep `1`, or change to the subscription QoS you need: `0`, `1` or `2`. |
| `<HTTP_BASE_URL_WITH_TRAILING_SLASH>` | API base URL, such as `https://api.example.com/v1/`. Keep the final `/`. |
| `<HTTP_REQUEST_PATH>` | Relative request path, such as `readings`. Avoid a leading `/` if the base URL includes a path. |
| `Method` in `Expression` | Defaults to `POST`; change it to the HTTP method your API expects. |
| `<DEVICE_ID>` | The device identifier to include in the HTTP body. |
| `<UNIT>` | The measurement unit, such as `C`. |
| API `Headers` | Optional fixed headers as string key/value pairs. |

The initial mapping expects a numeric UTF-8 payload, such as `23.5`, and sends a
JSON body containing `deviceId`, `temperature`, `unit`, `messageId` and `traceId`.
Use a decimal point and no unit suffix in the payload. The device ID is fixed in
this template; use one device's exact topic so other devices are not labelled with
the same ID.
Edit the expression's body to match your API. For a JSON object or another payload
format, the expression needs adapting before the trial. Use JSON/JSONata string
escaping when inserting values containing quotes or backslashes. The template sets
both resource timeouts to ten seconds; adjust them for the device and API if needed.

For first-time setup, publish and copy the template beside the executable under the
standard environment-specific configuration filename. Run from the repository root:

```powershell
dotnet publish samples/Hosting/FluxFlow.Hosting.Telemetry.Sample -c Release -f net10.0 --no-self-contained -o artifacts/telemetry-trial
Copy-Item samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/appsettings.trial.template.json artifacts/telemetry-trial/appsettings.Trial.json
```

Edit `artifacts/telemetry-trial/appsettings.Trial.json` and replace every placeholder.
Copy the template only once; repeating the copy would overwrite your filled-in file.
Use `-f net8.0` when publishing for .NET 8. The destination needs the matching .NET
runtime. Keep `appsettings.json` in the publish directory: `appsettings.Trial.json`
overrides it rather than replacing it. Use that exact filename and casing, especially
on Linux. The standard host treats the overlay as optional, so a missing file leaves
the original sample settings active.

Authentication is omitted by default. If needed, supply credentials through standard
environment-variable overrides in the same terminal before running. For MQTT username/
password and HTTP Bearer authentication, use only the settings relevant to your services:

```powershell
$env:FluxFlow__Resources__broker__Username = '<MQTT_USERNAME>'
$env:FluxFlow__Resources__broker__Password = '<MQTT_PASSWORD>'
$env:FluxFlow__Resources__api__Authentication__Scheme = 'Bearer'
$env:FluxFlow__Resources__api__Authentication__Token = '<HTTP_TOKEN>'
```

Environment variables override JSON settings; command-line values override both.
Use double underscores for nested environment keys, as above. These overrides apply
to the configuration keys, not to placeholder text inside an expression.
Use a fresh terminal or remove old trial overrides when switching devices or services.
For private CA/client certificates, see [MQTT setup](../../../src/FluxFlow.Configuration/Components/Mqtt/README.md).
For Basic or OAuth authentication, see [HTTP setup](../../../src/FluxFlow.Configuration/Components/Http/README.md).
Relative MQTT certificate paths are resolved beside the published executable.
Keep real credentials out of the checked-in template. The `artifacts` directory is
already ignored by Git.

After filling in the file and setting any needed credentials, run:

```powershell
dotnet artifacts/telemetry-trial/FluxFlow.Hosting.Telemetry.Sample.dll --environment Trial
```

For the first run, confirm the expected reading reaches your API, then stop with
Ctrl+C. You can append `--RunFor=00:01:00` for a one-minute trial. Workflow failures
are logged through application logging; HTTP responses remain on the existing output
port. The console is not an HTTP delivery log. The delivery limitations below still apply.

Startup validates configuration shape and supported setting values. Unfilled broker
or API address placeholders fail with the resource's configuration location; missing
required credentials (for example, a Bearer scheme without its Token) fail there too.
There is no general placeholder substitution or detection: a topic, client ID or
string inside the mapping can legally contain placeholder text. Replace all of them
before running. Successful startup does not prove authentication or delivery: MQTT
connection failures follow the reconnect policy, and HTTP requests happen only when
readings arrive. Check the broker and receiving API for the trial result.

### Trial success checklist

- [ ] A known device reading reaches the API with the expected value, unit and device ID, and the API returns its expected success status.
- [ ] The API receives the expected mapped body; `X-Flow-Message-Id` matches its `messageId`, and `traceId` is nonempty.
- [ ] After interrupting and restoring the broker connection, the subscription recovers and a newly produced reading reaches the API. Use a different value to distinguish it from retained data. This does not require replay of readings sent during the outage.
- [ ] Ctrl+C stops the host cleanly, and the broker confirms that the trial client disconnected.

Record the result of each check using the receiving API and broker observations.

## Errors, reconnect and shutdown

HTTP statuses, including non-success statuses, are ordinary responses on
`telemetry.send.Output`. Invalid numeric payloads become workflow errors and bypass
the HTTP call. HTTP transport failures and the configured HTTP timeout also produce
workflow errors. Later readings can still be processed.

The `send.Error` link feeds `errorRequest`, a JSONata Mapper with `HandleErrors: true`,
which converts the original error into a `LogRequest`. Normal mappers forward errors
unchanged, so mapping and subscriber errors reach this same link through the HTTP
component without making an HTTP request. They are logged once, rather than linking
each intermediate error output to the logger. `connection.Error` also feeds the error
mapper for connection failures.

The `log` component uses application logging, with no dedicated exporter. Its Error-level
message contains the original error code and message, trace ID and error message ID.
Category, transient status, details and correlation/causation IDs are supplied as log
attributes where available; displaying scope attributes depends on the host's logging
provider. Existing component diagnostics are unchanged. Successful readings and HTTP
error statuses such as 422 are not logged as workflow failures. The original output
messages remain available for observation. Failures in the logging branch are not
linked back to itself.

The MQTT resource automatically connects and retries with a one-second delay. After
connection loss it restores the current subscription, including after a broker restart.
`telemetry.receive.Events` reports `mqtt.subscriptions_applied` when ready. Shutdown
through Ctrl+C stops production, disconnects MQTT and drains component work; native DI
owns disposal.

Reconnect restores connectivity, not delivery of every reading. The sample uses a
clean MQTT session, has no durable inbox and does not retry HTTP POST requests. QoS 1
does not guarantee end-to-end HTTP delivery or exactly-once processing.

## Verify without external services

The existing MQTT test project loads this configuration and starts a local MQTTnet
broker and Kestrel HTTP API. Tests override endpoint addresses and the test client ID;
recovery cases also configure retries and an error destination. No Docker or
separately installed broker is needed:

```powershell
dotnet test tests/Unit/FluxFlow.Components.Mqtt.Tests -c Release --filter FullyQualifiedName~Telemetry
```

Both .NET 8 and 10 targets cover mapping and trace context, HTTP errors, mapping and
transport failures, subsequent recovery, broker restart with subscription restoration,
and shutdown with no connected MQTT client or unfinished components.
The failure/recovery check also captures application logging, verifies the error
message and IDs, and checks that each failure is logged once while healthy readings
and HTTP status responses produce no workflow-error log.

Recovery checks explicitly enable `RetryUnsafeMethods` for the telemetry POST and
verify identical bodies and message IDs across retries. Exhausted `503` responses
remain HTTP responses; exhausted transport failures reach the configured `Error`
destination. A later reading can still succeed. Normal shutdown drains retries;
canceling shutdown interrupts a pending retry. These test overrides do not enable
POST retries in the default sample or add durable delivery guarantees.

The same suite also publishes the sample for each target and runs the published DLL
in a separate process, from a different working directory. It verifies the copied
configuration, a retained MQTT reading mapped to an actual HTTP request, message and
trace IDs, a clean MQTT disconnect and exit code zero. The process uses `RunFor` for
graceful shutdown; a test deadline kills it only if it fails to exit. Existing process
test helpers capture stdout/stderr for failure diagnostics. This check runs as part
of the normal test suite in CI, without an additional job or external service.
The trial case also loads `appsettings.Trial.json`, overrides its addresses and
credentials through child-process environment variables, and verifies command-line
precedence for the client ID. A local broker and API verify MQTT credentials and
HTTP Bearer authentication. Registration checks cover invalid trial settings and
ensure these diagnostics do not include the supplied test credentials.

To run just the publish check:

```powershell
dotnet test tests/Unit/FluxFlow.Components.Mqtt.Tests -c Release --filter FullyQualifiedName~Telemetry_published
```

Input/link observation, elevated Windows Service verification and .NET 11 migration
remain deferred.

## Verify package consumption

The existing configuration consumer also runs this telemetry configuration from
locally packed NuGet packages on .NET 8 and 10:

```powershell
./eng/verify-configuration-consumer.ps1
```

The script creates an isolated consumer, package cache and local feed. All FluxFlow
dependencies must resolve from that feed at the candidate version; the consumer has
no project references. It verifies MQTT receipt, JSONata mapping, HTTP delivery,
message/trace context and host shutdown using a local broker and HTTP server. The
check runs in CI and does not publish packages to a remote feed.

An application using this configuration references `FluxFlow.Configuration`,
`FluxFlow.Expressions.Jsonata` and `Microsoft.Extensions.Hosting`. Configuration
currently includes all bundled component dependencies, including MQTT, HTTP and
Mapping; the expression engine is registered explicitly. The consumer's MQTT server
package and ASP.NET Core framework reference are test infrastructure, not requirements
of the telemetry console host.
