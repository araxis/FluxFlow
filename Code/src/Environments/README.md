# Local run environments

The environment layer prepares dependencies for a configuration snapshot. It is independent of Studio, Razor and the workflow engine. `FluxFlow.Environments.Testcontainers` starts and cleans up containers described by installed plugins. MQTT supplies Mosquitto and the RabbitMQ MQTT listener; RabbitMQ supplies AMQP; NATS supplies its broker and JetStream configuration; HTTP supplies WireMock.NET. `FluxFlow.Execution` runs the prepared application through the existing Generic Host registration and lifetime APIs. `FluxFlow.Runner` is a thin local companion executable.

## Execution telemetry

Logs use `ILogger` and the OpenTelemetry logging provider; traces use the existing `ActivitySource` instrumentation and OpenTelemetry tracer provider. The official `OpenTelemetry.Exporter.OpenTelemetryProtocol` exporters serialize both signals as OTLP/HTTP protobuf. A local HTTP transport handler retains the SDK export bytes in the originating run's bounded store. It does not rewrite messages, exceptions, timestamps, attributes or span data. The SDK's `OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES` option is enabled for this process to retain numeric `ILogger` event IDs; event names use OTLP's `event_name`. Structured logging scopes become OTLP attributes through the SDK.

Studio reads retained payloads from `/api/runs/{id}/telemetry/logs`, `/diagnostics` and `/traces`, with `application/x-protobuf` content. These are companion query endpoints returning standard `ExportLogsServiceRequest` or `ExportTraceServiceRequest` bodies, not OTLP ingestion endpoints. The bodies can be decoded with the unmodified [OpenTelemetry protobuf schemas](https://github.com/open-telemetry/opentelemetry-proto/tree/v1.11.1) kept under `FluxFlow.Environments/Telemetry/Protos`, alongside their license. Display models are projections of those payloads and are excluded from run-status JSON. Run status and workflow output samples remain application data on the existing API.

Engine logs and runner diagnostics have separate stores. Each retains the latest 1,000 records; traces retain the latest 1,000 completed spans. The query response's `X-FluxFlow-Dropped` header reports evicted records without adding fields to OTLP. Stop closes the run's stores after shutdown diagnostics are captured. Reads retain those records while the run remains available, and later records cannot enter a completed run. Starting another run uses new stores and a new run ID. None of these operations changes the project or its Undo/Redo history.

## Studio

The **Connections** menu lists named resources from the imported configuration. **Add connection** creates an MQTT or HTTP definition at application or workflow scope. Selecting a connection shows its address and the components that reference it; **Edit all settings** opens its complete JSON without dropping unrecognized properties. Connection edits participate in project Undo/Redo. These definitions do not create Docker containers.

The Environment menu opens three separate pages:

- **Registries** saves names, host addresses, descriptions and authentication choices in application settings. Test connection probes the registry's HTTPS `/v2/` endpoint; it reports reachability and authentication requirements, not whether saved credentials can pull a particular image. Sign in/out uses the local companion and Docker CLI. Sign-in requires Docker's configured credential helper and passes the password/token through standard input. Credentials are never stored in Studio settings or project files. Sign-out affects Docker's shared login for that registry.
- **Resource defaults** saves reusable profiles in application settings. Choose a **Connection type**, then a compatible **Server implementation**: MQTT offers Mosquitto or RabbitMQ with MQTT enabled; HTTP offers WireMock.NET. A profile may optionally be the default for a connection type, shared across its server choices; only one default is allowed per type. Existing image-only profiles are preserved, but new profiles require a supported type. Testcontainers' Ryuk cleanup remains managed by Testcontainers; its private-registry override uses the companion's existing `TESTCONTAINERS_RYUK_CONTAINER_IMAGE` configuration.
- **Project setup** lists connections discovered from the engine configuration. Choose **Use existing service** or **Start Docker container** first; saved profiles are an optional shortcut. **View connection** returns to the definition and its usages. Applying a profile copies its resolved image and customization into one undoable project change. Later application-default or registry changes do not modify that copy. A registry can also be applied directly to a project's image; the project retains the full image address, not credentials or a live registry reference. Run status shows each resolved endpoint and its Docker container ID when applicable.

Existing services are never created, stopped or removed by the environment layer. Project execution currently uses the existing in-process local companion; other runner modes remain separate work. Application registry/default edits autosave without entering project Undo/Redo. Project changes retain the normal Save and Undo/Redo behavior.

### MQTT services in Host definitions

An MQTT service supplies the endpoint for each application connection bound to it. TLS follows the endpoint scheme (`mqtts` or `wss`); the TLS control updates that scheme. **Use connection** retains the application's credentials and certificate settings, including for older saved services. **Service settings** instead supplies the service's username/password, optional client certificate and trusted CA paths, and certificate revocation mode. Empty service credentials mean anonymous access; an empty trusted CA list uses system trust. Certificate paths are resolved on the execution host using the existing MQTT configuration APIs.

Service settings are copied into the environment's locked host snapshot and saved with the project, including passwords. They configure client authentication; they do not create broker users. Selecting a different service or removing the binding leaves the original application connection unchanged. Only the per-instance run configuration receives the service settings, using the existing `Address`, `Username`, `Password`, `ClientCertificate`, `TrustedCertificates` and `CertificateRevocationMode` fields. Client ID, reconnect, session and other application settings remain unchanged. Host definition edits do not modify existing snapshots; project placement and binding changes support Undo/Redo.

The Connections page separates the saved manual definition from **Effective settings** for the selected environment's next run. Each application instance shows its binding and the source of its endpoint and MQTT security settings. Passwords show only whether they are configured. Missing bindings are reported instead of silently falling back; container settings remain assigned at startup. This read-only preview does not represent an already-running snapshot or change project history.

For the telemetry sample, import `Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/appsettings.json` with application section `FluxFlow`. Choose Mosquitto for `Resources:broker` and WireMock.NET for `Resources:api`, then import the adjacent `wiremock.mappings.json`. Start the application and publish a numeric payload such as `23.5` to `sensors/trial-device/temperature` at the displayed broker address. The mapped request can be inspected at the displayed HTTP address plus `/__admin/requests`.

For containers, select a configured registry, then enter the image name and tag or digest. New profiles start with `latest`; opening a saved profile preserves its version. Changing server implementation replaces a stock image with the new server's image; custom image names and digests remain unchanged and must be compatible with the selected server. **Use existing Docker login** is the default; credentials must be available to the local companion's user account. Optional username/token fields appear only when requested. **No Studio sign-in** disables Studio's sign-in controls, but does not force anonymous pulls or bypass Docker's saved credentials. Existing authentication preferences remain saved unchanged. Under **Advanced image settings**, uncheck **Pull image if missing** for environments that must use only cached images. There is no fallback to another registry.

WireMock profiles in **Resource defaults** and connections in **Project setup** use the same endpoint editor. **Edit endpoints** opens the shared JSON editor for WireMock.NET mappings. **Import mappings** captures a JSON file's contents into the profile or project, without retaining a dependency on its local path. A single mapping or an array is accepted, for example:

```json
[
  {
    "Request": { "Path": "/readings", "Methods": ["post"] },
    "Response": { "StatusCode": 201, "Body": "accepted" }
  }
]
```

These are WireMock.NET mappings, not the Java WireMock format. Mappings are installed through its admin API before the workflow starts. No endpoints are guessed from HTTP client components.

To make a reusable preset, create a WireMock profile such as **P2**, configure its image and mappings, then use **Lock project setup** beside the profile title. The profile remains editable in Resource defaults. In Project setup, **Use profile** applies a self-contained snapshot with its name, identity, lock policy and resolved settings in one Undo/Redo action. Later profile edits, removal or registry changes do not change that snapshot; apply the profile again explicitly to update it.

Locked snapshots expose **View endpoints** and prevent project overrides of the image, provider, pull policy, mappings, startup files and variables. Shared validation rejects conflicting overrides before any container starts. Dynamic host ports remain independent per run. Unlocked snapshots allow project customization without changing the original profile. The lock is an editing and validation rule, not an authorization boundary against someone rewriting the snapshot itself.

**Startup files** accepts an object mapping absolute container file paths to text contents. Files are copied into the container before startup, without mounting host directories. **Environment variables** accepts an object of names and string values. Both are project-specific. For example, a Mosquitto file override can be:

```json
{ "/mosquitto/config/mosquitto.conf": "listener 1883\nallow_anonymous true\npersistence false\n" }
```

The built-in broker uses this anonymous, non-persistent configuration by default. These providers expose a plain MQTT listener on container port 1883 and WireMock.NET HTTP/admin on port 80. Custom images must retain those service contracts. Published host ports are dynamic and bound to loopback.

The `rabbitmq-mqtt` provider supplies the same MQTT connection contract using RabbitMQ. It installs `/etc/rabbitmq/enabled_plugins` with `rabbitmq_mqtt` and `/etc/rabbitmq/rabbitmq.conf` with a plain MQTT listener and anonymous access for this temporary local environment. It waits for broker startup and the internal MQTT port before starting the workflow. Project startup files can override either file; custom configuration must retain the MQTT listener on port 1883 and the authentication expected by the local runner. No AMQP or management port is published. Containers and their data are removed on Stop; RabbitMQ images remain cached.

Each server has a shared configurator in Resource defaults and Project setup. For Mosquitto and RabbitMQ, add test users and permissions directly, then select the **Workflow connection user**. Mosquitto uses MQTT topic filters, one per line; RabbitMQ uses regular expressions on `amq.topic` routing keys in vhost `/` (MQTT `/` separators become `.`). Empty permission fields deny access. RabbitMQ also grants access to MQTT subscription queues. Adding or editing a user automatically configures authentication, disables anonymous connections and generates hashed password files or user definitions before startup. Access settings are stored separately per implementation, so changing server does not reinterpret or discard the other server's permissions.

These are test credentials: passwords are saved in application settings and project snapshots so the workflow can authenticate. They are not a secret vault. Only the run's configuration copy receives the selected credentials; exported engine configuration and displayed endpoints are unchanged. When no users are configured, use **Workflow username/password** with file-based authentication. Removing the last user restores the existing startup-file or server defaults; saved file credentials are retained.

The server-specific file editors support edit, import and reset: Mosquitto configuration, hashed password file and ACL; RabbitMQ configuration, enabled plugins and definitions. Keep MQTT on port 1883. Custom RabbitMQ definitions require `definitions.import_backend = local_filesystem` and `definitions.local.path = /etc/rabbitmq/definitions.json` in its configuration. User settings and overrides of the files they generate cannot conflict; startup validation rejects a conflict rather than overwriting either. Reset a conflicting file override or remove the configured users. RabbitMQ's plugin file remains customizable. Locked project snapshots expose the same configurators read-only, and late dialog results are rejected after setup changes.

Environment edits participate in project Undo/Redo and are saved by **Save project**. Engine export contains only the workflow configuration. Run captures the current application and environment settings; subsequent editor changes affect the next run. Resolved addresses never replace the saved resource addresses. Managed resources use local mock endpoints rather than credentials intended for the original service; custom HTTP headers other than Authorization are retained.

Run starts all workflows in the application in-process inside the local companion. Stop drains/stops the host, disposes it, then removes owned containers. Startup failures and cancellation also release owned containers. Images remain cached. A fresh run creates fresh instances. Studio polls run status; a run abandoned for two minutes is cleaned up by the companion. Host shutdown also stops its runs. Testcontainers' resource reaper provides crash cleanup. Cleanup errors remain visible rather than being reported as success.

The current Studio web host includes the companion endpoints, so opening Studio from its local URL runs everything on that computer. The shared UI depends only on `IEnvironmentRunner`; desktop hosts can provide it directly. A remotely hosted website is not connected to a user's local companion by this slice. Cross-origin pairing, service/container execution modes, keep-running environments and live debugging remain separate work.

## Headless use and tests

**Check environment** in Project setup validates the current configuration and environment through the companion without starting a run. It reuses provider validation for users, permissions, startup files and locked snapshots, then pings Docker only if containers are selected. It does not pull images, authenticate to registries, or connect to existing services. Results are cleared after project edits; Run still validates its own snapshot. Check and Cancel do not change configuration or Undo/Redo history.

Run the standalone companion with `dotnet run --project Code/src/Hosting/FluxFlow.Runner -f net10.0`. It binds to `http://127.0.0.1:5295` by default. Use a client-generated run UUID:

- `POST /api/runs/{id}` with `{ "Configuration": "<application JSON>", "Environment": { "Resources": { "Resources:broker": { "Provider": "mosquitto" }, "Resources:api": { "Provider": "wiremock", "Mappings": "<mapping JSON>" } } } }`.
- `GET /api/runs/{id}` for status and resolved addresses. Poll while the run is in use.
- `POST /api/runs/check` with the same request body to check setup and, when required, Docker availability without creating a run.
- `DELETE /api/runs/{id}` to cancel startup or stop and clean up.

All calls require `X-FluxFlow-Run: 1`. Only loopback clients and same-origin browser requests are accepted. Resource keys are configuration paths, including workflow-local paths such as `Workflows:main:Resources:broker`.

The integration tests reuse the MQTT-to-HTTP telemetry sample with both Mosquitto and RabbitMQ. Docker must be running with Linux containers and these images cached: `eclipse-mosquitto:2.0.22`, `rabbitmq:latest`, `sheyenrath/wiremock.net-alpine:2.14.0`, and the Testcontainers resource reaper image. Tests do not pull application images. Run:

```text
dotnet test Code/tests/Integration/FluxFlow.Environments.Tests -f net8.0
dotnet test Code/tests/Integration/FluxFlow.Environments.Tests -f net10.0
```

Tests exercise the shared Studio run session through the companion HTTP API, covering telemetry delivery, custom mappings/files/variables, Stop, fresh execution and cleanup after missing-image or workflow-startup failures. They also verify unchanged project configuration and Undo/Redo history. Additional checks cover reverse cleanup, cancellation, companion shutdown and local endpoint restrictions. Non-Docker checks can run with `--filter "Category!=Docker"`.

To add a managed service, register its `EnvironmentProviderDescription` and `IResourceEnvironmentAdapter` in the plugin’s portable design. The same description drives the Studio profile and project editors. Register these contributions and a `ContainerServiceDefinition` from the plugin’s host entry. The definition supplies validation, startup files, command, runtime variables and readiness; the host supplies Docker and owns the container lifetime. Removing a plugin removes its editors and provisioning after restart. Saved profiles and locked project snapshots remain available for reinstall. Execution never disposes externally supplied services.
