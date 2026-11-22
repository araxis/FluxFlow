# Configuration-driven workflow stop/restart

Run from the repository root with .NET 8 or .NET 10 installed:

```powershell
dotnet run --project samples/Hosting/FluxFlow.Hosting.Sample -f net10.0
```

Use `-f net8.0` for .NET 8. In Visual Studio, open `FluxFlow.sln`, find
`samples / Hosting / FluxFlow.Hosting.Sample`, and select it as the startup project.
The solution folders match the disk layout. `appsettings.json` is copied beside the
executable, and the host uses that directory as its content root, regardless of the
shell's working directory.

## What runs

`appsettings.json` selects two independent flows:

```text
main.clock (Scheduler) -> main.generate (DataGenerator) -> main.report (ReadingReporter)
background.clock (Scheduler) -> background.generate (DataGenerator) -> background.report (ReadingReporter)
```

The host explicitly registers those three component types, selects the `FluxFlow`
configuration section, and calls `AddFluxFlow`. That section contains the ordinary
`Workflows` structure; the outer section is a host choice, not a new engine model.
Standard host configuration overrides work, for example
`--FluxFlow:Workflows:main:clock:Interval=00:00:00.500` after `dotnet run --`.
Configuration is applied at startup; changing it does not rebuild the running flow.
The main workflow generates temperature readings; background generates voltage readings.
All links stay within their own workflow, allowing independent stop/restart.

`Program.cs` owns hosting, registration, discovery and observation. `ReadingReporter.cs`
is a sample-only business node using native Dataflow blocks and the existing FlowLifetime.
It forwards readings unchanged and emits a `reading.received` domain event with a sequence
number and the reading as Details. This makes Events observable without a broker or other
external service. No new framework component, hosting abstraction or registry is introduced.

## What to expect

1. Registered component and input/output addresses print before startup. Discovery does
   not instantiate components and does not report their runtime readiness.
2. Standard Generic Host startup composes both workflows, connects links and starts their
   schedulers. Temporary Output and Events probes print three samples per port from each
   reporter as serialized FlowMessage envelopes, including IDs and event address headers.
   These are observations, separate from the host's unchanged logs.
3. `main.StopAsync()` drains main and disposes its workflow scope. Background stays running.
   New background probes require three readings and events timestamped after main stopped,
   so a retained broadcast value cannot alone satisfy the continued-output check.
4. `main.StartAsync()` creates fresh components from the existing registrations and reconnects
   main's links. The sample verifies that main's reporter changed and background's reporter
   stayed the same. New main probes then receive three fresh readings and events. Each
   observation phase disposes its probes; probes from an earlier run are never reused.
5. Host shutdown drains both workflows and disposes their scopes. The sample checks both
   completion tasks and prints `HOSTING_SAMPLE_VERIFIED`. Shared resources remain owned by
   the root provider until host disposal. Ctrl+C can request shutdown earlier; an interrupted
   demonstration does not print the verification marker. Observation and workflow operations
   share a 15-second deadline, and shutdown has its own 15-second deadline.

The existing CI steps run this same program on .NET 8 and 10. A failed check or timeout
exits unsuccessfully. No additional test harness, project or hosting API is introduced.

Each probe buffers up to eight messages and reports dropped samples. It observes output
offers, not confirmed business delivery. Serialization runs on the reader side. Detaching
does not pause processing or guarantee replay of missed messages. This is an automatic
console demonstration, not a remote transport or interactive UI.
Output and event rows are independent streams, not guaranteed matched pairs. A workflow
restart is different from detaching a viewer: it drains and replaces that workflow's blocks,
and resets the sample reporter's sequence. Configuration is unchanged throughout.

Hot reload, input/link observation, the real-device trial and actual Windows Service
verification remain deferred.
The separate `eng / hosting-consumer / HostingConsumer` solution entry is the existing
lifecycle smoke check used by `eng/verify-hosting.ps1`; it does not load this sample.
See [hosting and observability](../../../../docs/05-hosting-and-observability.md) for the
host contract and the distinction between console fallback and real SCM verification.
