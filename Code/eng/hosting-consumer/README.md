# Host lifecycle smoke check

This verification executable is visible under `eng / hosting-consumer / HostingConsumer`
in `FluxFlow.sln`, matching its existing disk location. The driver is also visible under
the solution's `eng` folder:

```powershell
./eng/verify-hosting.ps1
```

The existing CI step runs this non-elevated command. It publishes for .NET 8 and 10,
then checks normal hosting and Windows-adapter console fallback, including output/event
probes, business delivery, shutdown and DI disposal. Evidence is written to a fresh
temporary directory reported by the script. No service is installed in these modes.

This is a finite verification program with test nodes. For configuration-driven usage,
see the [console host sample](../../samples/Hosting/FluxFlow.Hosting.Sample/README.md).
Input/link observation and actual Windows Service verification remain pending. The
separate elevated `-WindowsService` check is not part of ordinary CI.
