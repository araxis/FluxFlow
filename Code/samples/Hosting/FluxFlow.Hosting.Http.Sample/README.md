# Configuration-driven HTTP host

This sample runs an ordinary ASP.NET Core host with three configured request/reply flows:

```text
POST /devices/{id}/readings -> HttpTrigger.Output -> Mapper.Input
                             HttpTrigger.Response <- Mapper.Output

POST /uploads             -> HttpTrigger.Output -> Mapper.Input
                             HttpTrigger.Response <- Mapper.Output

POST /gateway/devices/{id}/readings
    -> HttpTrigger -> JSONata request mapper -> HTTP Client
    -> JSONata reply mapper -> HttpTrigger.Response
```

The routes, reply timeouts, links and JSONata expressions live in `appsettings.json`.
`Program.cs` registers the existing component/resource types and expression engine, then maps
the configured endpoints. ASP.NET Core owns the server and host lifecycle.

## Run

From the repository root:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Http.Sample -f net10.0 --no-launch-profile
```

Use `-f net8.0` for .NET 8. In Visual Studio, select
`samples / Hosting / FluxFlow.Hosting.Http.Sample` as the startup project. The solution
folders match the disk layout. Visual Studio uses the launch profile's addresses;
use the address printed at startup in the requests below. The command above skips
that profile and uses the configured `http://127.0.0.1:5080` address.
The configuration is copied beside the executable;
the host uses that directory as its content root. To change the listening address,
append `-- --urls http://127.0.0.1:5081` to the command.

In another PowerShell window:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5080/devices/sensor-7/readings `
    -ContentType application/json -Body '{"value":23.5,"unit":"C"}'
```

The response is HTTP 200 with `X-Device-Id: sensor-7` and a JSON body:

```json
{
  "deviceId": "sensor-7",
  "reading": 23.5,
  "unit": "C",
  "messageId": "<generated message ID>",
  "traceId": "<generated trace ID>"
}
```

The mapper reads `RouteValues.id` and the JSON `Body` directly, and uses the standard
`$messageId` and `$traceId` expression variables. It returns the HTTP reply shape;
the existing mapper preserves correlation so each request receives its own reply.
The sample echoes readings; it does not persist them or validate a device schema.

Press Ctrl+C to stop. Native hosting stops FluxFlow and DI disposes its components.
Malformed JSON returns 400; an unsupported method returns 405; a missing route returns
404. A reply timeout returns 504. Configuration is applied at startup.

## HTTP-to-serial commands

The optional `SerialCommands` section uses the same host and existing components:

```text
POST /serial/commands -> HttpTrigger (Acknowledge) -> JSONata Mapper -> SerialText.Input
                                                     serial.Error -> errorRequest -> Logger
```

Select this section explicitly; the default `FluxFlow` section does not open serial
hardware. From the repository root:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Http.Sample -f net10.0 --no-launch-profile -- `
    --WorkflowSection=SerialCommands `
    --SerialCommands:Workflows:commands:Resources:port:PortName=COM3
```

Use `-f net8.0` for .NET 8 and set the port name for your machine, such as `COM3`
or `/dev/ttyUSB0`. The workflow-local port uses 115200 baud, 8N1. Send a command:

```powershell
Invoke-WebRequest -Method Post -Uri http://127.0.0.1:5080/serial/commands `
    -ContentType application/json -Body '{"Text":"MOVE 1"}'
```

HTTP **202 acknowledges workflow admission only**. It does not confirm a serial
write, device receipt or command execution. The mapper requires a string `Text`;
SerialText rejects embedded CR/LF and text exceeding its configured byte limit.
Accepted valid commands are written sequentially, with one LF appended by default.
The JSON property name is `Text`; values and their casing are preserved.

Malformed JSON returns 400 before admission. A valid JSON body with an invalid
command still receives 202; the subsequent mapping/validation error is logged via
`serial.Error` without writing to the port. No correlated response or device reply
protocol is configured. Received serial lines have no consumer in this scenario.
Port failures fault execution; there is no automatic reconnect or write retry.
Unlike command validation errors, fatal I/O failures trigger the default host's
shutdown path. Queued serial writes are dropped; a native write already in progress
may still finish, and a timed-out write may have sent only part of the command.
An earlier 202 response cannot be changed. New requests receive 503 while the HTTP
listener remains available; once the host stops listening, HTTP responses are no
longer available.

On normal stop, `FlowWorkflow.StopAsync` drains accepted work. While `commands` is
stopped, the route returns 503; `StartAsync` creates a fresh serial session and
restores admission after a successful stop. A faulted run cannot be restarted this
way: correct the cause and run a new host/application instance. Commands are not
replayed automatically. Ctrl+C uses normal host shutdown. The `DemonstrateDrain` option
below belongs to the default request/reply sample, not this command variant.

Automated checks use simulated serial I/O and the real ASP.NET Core endpoint to
verify acknowledgement before a write finishes, command bytes, errors, stop/restart,
and shutdown. Simulated read/write failures and write timeouts also verify queued
writes are dropped, HTTP admission returns 503 while listening, and resources are
closed and disposed once. Native configuration is built without opening a port.
Hardware and driver checks remain pending; the commands above are for a later hardware trial.

## Drain, stop and restart

The reading workflow's `api.receive` trigger sets `"ShutdownMode": "Drain"`.
Choose the mode per trigger:

- `Cancel` (the default) cancels pending HTTP waits with 503 when stopping.
- `Drain` rejects new requests with 503 and allows accepted requests to receive their
  replies. Existing reply timeouts, client cancellation and the stop deadline still apply.

Run the optional automatic demonstration using a local ephemeral port:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Http.Sample -f net10.0 --no-launch-profile -- `
    --DemonstrateDrain=true --urls http://127.0.0.1:0
```

Use `-f net8.0` for .NET 8. The program sends a reading (200), calls
`FlowWorkflow.StopAsync`, verifies another reading receives 503, calls
`StartAsync`, and verifies a fresh request succeeds (200). The host and its routes
stay running through the workflow restart. It then stops the host, prints
`HTTP_DRAIN_SAMPLE_VERIFIED`, and exits. A failed check exits unsuccessfully.
No downstream HTTP server or MQTT broker is needed; use the default environment.

The mapper replies immediately, so this demonstration stops an idle workflow; it
does not deliberately hold an in-flight request. The existing HttpTrigger tests
cover accepted requests draining during stop, concurrent out-of-order replies,
503 admission, timeout/cancellation cleanup and restart. The sample configuration
and restart test run in the existing hosting test suite on .NET 8 and 10.
Other sample triggers retain the default `Cancel` mode. Without the demonstration
option, the sample remains an ordinary interactive server.

## Upload form fields and files

The `uploads` workflow accepts multipart requests at `POST /uploads`. From the repository
root in PowerShell, upload this sample's configuration as a small example file:

```powershell
curl.exe --fail-with-body http://127.0.0.1:5080/uploads `
    -F 'tag=robotics' -F 'tag=bench' `
    -F 'upload=@samples/Hosting/FluxFlow.Hosting.Http.Sample/appsettings.json;type=application/json'
```

On Linux or macOS, use `curl` and shell-appropriate line continuation. Repeat the
`upload` argument to send multiple files, including multiple files with the same field name.

The response is HTTP 200 with:

- `fields`: all form fields as string arrays, such as `"tag": ["robotics", "bench"]`.
- `files`: a summary for each file with `name`, `fileName`, `contentType` and `length`
  (original byte count). It stays an array for zero, one or multiple files.
- `messageId` and `traceId`: the message context used by the mapper.

HttpTrigger supplies the parsed form and owned file snapshots. JSONata selects the
summary with `[Files.{'name':Name,'fileName':FileName,'contentType':ContentType,'length':Length}]`;
the explicit brackets preserve the array shape. The response excludes raw body and
Base64 file content. Files are not persisted by this sample. The existing 1 MiB total
request limit applies, including multipart headers and boundaries.

The tests load this same configuration and exercise concurrent uploads, repeated fields,
multiple files, empty files and shutdown on .NET 8 and 10. See the
[HTTP hosting guide](../../../src/Hosting/FluxFlow.Hosting.AspNetCore/README.md) for
the complete upload shape and limits.

## Forward a reading to a downstream HTTP API

The `gateway` workflow sends a request to the shared `downstream` HTTP client resource.
Its default `BaseAddress` is `http://127.0.0.1:5090/`. To use your own API, override it
when starting the sample:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Http.Sample -f net10.0 --no-launch-profile -- `
    --FluxFlow:Resources:downstream:BaseAddress=http://127.0.0.1:5090/
```

The downstream API must accept `POST /devices/{id}/readings`. With that API running:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5080/gateway/devices/sensor-7/readings `
    -ContentType application/json -Body '{"value":23.5,"unit":"C"}'
```

The request mapper constructs the downstream path, escapes the device ID and sends
`{"deviceId":"sensor-7","value":23.5,"unit":"C"}` as JSON. It adds
`X-Flow-Message-Id` from the message context. Incoming headers such as Authorization
and Cookie are not copied; downstream authentication belongs to the HTTP resource.

The reply mapper returns the downstream status, body text and Content-Type. It does
not parse or evaluate the downstream body as an expression. JSON responses therefore
remain JSON on the HTTP connection; plain text remains plain text. Other downstream
headers are not forwarded by this sample.

The existing error and timeout contracts apply:

- Downstream HTTP errors (such as 422 or 503) remain HTTP responses with their bodies.
- Connection failures and the HTTP client's two-second timeout produce a workflow
  error, which HttpTrigger returns as an empty 500 response without exception details.
  Ordinary mappers forward these error messages unchanged.
- HttpTrigger's independent five-second reply deadline returns an empty 504. It ends
  the caller's wait, not work already sent downstream; the HTTP client's timeout still
  bounds that work. Late replies cannot complete another request.

Retries are not enabled for this POST example. No downstream server is started by
the sample, and no HTTP requests are sent merely by starting it. The reading and
upload demonstration routes work independently of downstream availability.

The gateway tests start their own local Kestrel server on an ephemeral port and load
this same configuration, overriding only the downstream address and, for the reply
deadline test, that deadline. They require no external service:

```powershell
dotnet test tests/Unit/FluxFlow.Hosting.AspNetCore.Tests -c Release --filter FullyQualifiedName~HttpGatewayTests
```

Both .NET 8 and 10 targets verify success, concurrent correlation, HTTP error responses,
disconnects, client timeouts, reply deadlines, recovery and shutdown.

Input/link observation and elevated Windows Service verification remain deferred.

## Publish a device command to MQTT

The optional `Commands` environment adds this workflow using
`appsettings.Commands.json`, alongside the existing routes:

```text
POST /devices/{id}/commands
    -> HttpTrigger -> JSONata request mapper -> MQTT Publisher
    -> JSONata reply mapper -> HttpTrigger.Response
```

Start an MQTT broker, then run the same host with the standard environment overlay:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Http.Sample -f net10.0 --no-launch-profile -- `
    --environment Commands --FluxFlow:Resources:broker:Address=mqtt://127.0.0.1:1883
```

Use `-f net8.0` for .NET 8. Give concurrent sample instances unique
`FluxFlow:Resources:broker:ClientId` values. Broker authentication and TLS use the
existing [MQTT resource settings](../../../src/FluxFlow.Configuration/Components/Mqtt/README.md).
Without this overlay, the sample does not create an MQTT connection.

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5080/devices/motor-1/commands `
    -ContentType application/json -Body '{"Command":"Move","Arguments":{"Position":90}}'
```

The mapper requires a nonempty string `Command` and copies optional `Arguments`
unchanged. It publishes JSON containing `Command`, `Arguments`, `MessageId` and
`TraceId` to `devices/motor-1/commands`, with QoS 1 and no retain flag. The device ID
is URL-escaped to keep it within one MQTT topic level. Incoming HTTP headers are not
forwarded. Property names in `Arguments` are preserved.

HTTP 200 returns `Topic`, the native MQTT `ReasonCode` and `TraceId`. It confirms
broker acknowledgement of publication, **not device receipt or command execution**.
The broker can acknowledge a publication even when no device subscribes (for example,
`ReasonCode: NoMatchingSubscribers` with MQTT 5). The HTTP
reply's trace matches the published payload; each request keeps its own correlation.

Malformed JSON returns 400. Invalid command shapes and MQTT publication failures use
the existing workflow-error contract: empty HTTP 500, without exception details.
The five-second reply deadline returns 504; shutdown cancels outstanding waits with
503 while the HTTP connection is available. A timeout does not undo publication:
the command may still arrive, and a late acknowledgement cannot complete another
request. Reconnect restores the connection but does not replay failed publications.
There is no application-level command retry or device acknowledgement protocol.

The existing MQTT tests load both sample configuration files, start a local broker
and Kestrel, and verify concurrent correlation, payloads, validation failures,
broker outage/recovery, publication authorization rejection, late acknowledgements
and shutdown on .NET 8 and 10. The rejection check verifies HTTP 500 without error
details, correlation cleanup and a subsequent successful command on the same connection:

```powershell
dotnet test tests/Unit/FluxFlow.Components.Mqtt.Tests -c Release --filter FullyQualifiedName~Http_commands
```

No external service or physical device is needed for these tests.
