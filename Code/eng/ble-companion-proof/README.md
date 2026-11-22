# Windows BLE companion proof

An isolated Windows executable using `AddLocalFlowRunner`, the existing Generic Host,
standard `ILogger`/OpenTelemetry registration and Windows BLE APIs. The native
lifecycle implementation is shared with `FluxFlow.Components.Bluetooth.Windows`;
the proof remains an independent diagnostic executable. It does not read
or modify project files, infrastructure settings, browser storage or Undo/Redo.
Docker is not required. The radio is never enabled or disabled by this executable.

## Run

From the repository root:

```powershell
dotnet build Code/eng/ble-companion-proof/BleCompanionProof.csproj
dotnet run --no-build --project Code/eng/ble-companion-proof/BleCompanionProof.csproj -f net8.0-windows10.0.19041.0
dotnet run --no-build --project Code/eng/ble-companion-proof/BleCompanionProof.csproj -f net10.0-windows10.0.19041.0
```

Requires Windows 10 build 19041 or later, the corresponding .NET runtime and a BLE
adapter with the required role. Windows driver, policy and access restrictions can
still prevent operations even when the adapter reports role support.

The default proof performs:

- Cancellation before acquisition, ownership transfer and disposal of a late native
  result using a controlled disposable test handle. These are lifecycle logic tests,
  not proof of a physical device disconnect.
- Cancellation before scan, server and client startup.
- Regression checks for transient startup events, stale Stop events, closed-session
  callbacks, and preservation of both operation and cleanup failures.
- Native adapter and radio capability checks.
- Two passive scan/cancel/stop cycles, with late-record rejection checks. Only counts
  are logged; nearby device names and addresses are not retained.
- GATT service/characteristic creation with an injected startup failure, followed by
  up to two advertise/cancel/stop cycles. An unconfirmed Stop prevents any further
  server attempt in that process, including the extended session. The custom characteristic exposes only a constant,
  non-sensitive UTF-8 value: `FluxFlow BLE proof`.

Exit codes are 0 for no failures/limitations, 1 for an unexpected failure or invalid
arguments, and 2 when hardware or independent peer verification is incomplete.
Ctrl+C requests host cancellation. Cleanup does not reuse the cancelled run token.

## Independent peer

To keep the local server advertising for a manual client test, append `-- --serve 60`
to either `dotnet run` command. This runs the lifecycle checks first, then advertises
for 60 seconds (maximum 120). From a separate BLE central, discover and read:

- Service: `3f63b38e-98df-4a52-a9f8-76b803a934b2`
- Characteristic: `f50d7529-4e5d-4088-b88f-e236cd1e7f29`
- Expected UTF-8 value: `FluxFlow BLE proof`

To exercise this executable as the client, append:

```text
-- --read <12-hex-address> <public|random> <service-uuid> <characteristic-uuid>
```

Use an explicitly selected test peripheral and its current address type. There is
no automatic connection, pairing or writing to discovered devices. The client does
uncached service and characteristic discovery, reads once, and disposes all acquired
service/device handles on success, failure or cancellation. It reports byte count,
not payload. Confirm the expected value separately if testing an arbitrary device.
Do not assume that one local adapter can connect back to its own advertised server.

## Cancellation and cleanup limits

Windows documents that the connection process cannot currently be cancelled. The
client's 20-second cancellation request is therefore not a hard native-I/O deadline.
It rejects results after cancellation, but awaits native completion to acquire and
dispose any late handles instead of abandoning them. A blocked Windows request can
delay host shutdown. No immediate physical disconnect is claimed: other applications
may also hold device references.

Scan cleanup awaits the watcher stop event and detaches handlers. Server cleanup calls
`StopAdvertising` only after requesting advertising, and requires a `Stopped` event
or state with a separate 10-second deadline. An unconfirmed Stop is a failed check,
not a successful cleanup. Startup waits for a real started event; an interim
`Aborted` event with `Success` does not itself settle startup. Native status/error
events are logged unchanged, and event handlers are removed on exit.
Closing a session also gates callbacks already queued before detachment. Stop events
received before the cleanup phase cannot confirm a subsequent Stop request. If both
the operation and cleanup fail, an aggregate retains both original exceptions;
cancellation alone cannot turn failed cleanup into a passing check.
`StartedWithoutAllAdvertisementData` is reported as a discovery limitation. The GATT
provider has no `IDisposable` API; this proof attempts to verify advertising shutdown, not
immediate removal of every OS-cached service record. Process exit bounds the lifetime
of remaining native provider references. A separate peer must confirm over-the-air
delivery and behavior after Stop.

Live peer writes, notifications, bonding, device reconnection and multiple adapters
are outside this hardware proof. Component and companion tests separately exercise
workflow-backed values and subscriptions with controlled providers.

## Completion audit — 2026-11-02

The hardware-independent proof now has thirteen checks on .NET 8 and 10. They include
multi-service reverse rollback, cancellation during late provider acquisition,
fresh-provider reruns, preservation of simultaneous startup/cleanup failures,
outstanding request draining and rejection of late callbacks. All thirteen pass
on each runtime. `--no-hardware` deliberately returns a non-success result for the
unverified hardware portion; it does not report physical cleanup as verified.

## Verification record — 2026-11-01

| Check | .NET 8.0.33 | .NET 10.0.14 |
|---|---|---|
| Build | Passed, no warnings | Passed, no warnings |
| Eight hardware-independent lifecycle checks | Passed | Passed |
| Existing `ExecutionLogTests` | 13 passed | 13 passed |
| Native adapter capability query | Passed | Passed |
| Two scan/cancel/Stop cycles; reject late records | Passed | Passed |
| Injected server failure before advertising | Passed after cleanup fix | Passed after cleanup fix |
| Native GATT advertising startup | Reached `Started` | Reached `Started` |
| GATT Stop confirmation | Failed: status remained `Started` | Failed: status remained `Started` |
| Reject another invocation after unconfirmed Stop | Passed | Passed |
| Independent GATT client/server delivery | Not verified | Not verified |

Windows build 26300; installed adapter: Qualcomm FastConnect 7800 Dual Bluetooth
Adapter. Windows reported LE, central and peripheral support as true. The initial
radio-off trial was followed by an authorized temporary radio-on trial; an external
supervisor restored Off after both child processes exited and confirmed Off again
after three seconds. The proof itself does not change radio settings. No nearby
device was connected, and no independent test peripheral was available.

The live trial exposed an invalid `StopAdvertising` call before advertising had
started; cleanup now only calls it after a start request. Windows also emitted
`Aborted / Success` followed by `Started / Success` during startup. Waiting for the
started event resolved the premature startup rejection.

On both runtimes, `StopAdvertising` returned but the status remained `Started` and no
Stopped event arrived within 10 seconds. A separate minimal native-only process,
with fresh UUIDs and no companion registrations, reproduced the same symptom even
after 20 seconds. This resembles the Microsoft-confirmed status-reporting bug linked
below, but does not prove that the same cause applies on this OS/driver. Without an
independent observer, over-the-air shutdown cannot be claimed.

After review, the proof reported twelve passed checks, one failed Stop-confirmation
check, and three limitations (skipped server retries and two missing-peer checks)
per runtime (exit code 1). All proof processes
exited. Advertising startup is demonstrated; reliable in-process GATT Stop reporting
and client I/O cancellation remain unresolved or unverified. These results are not
a production-readiness pass for the server role.

References:

- [Windows GATT client APIs and connection cancellation limits](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
- [Windows GATT server APIs](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server)
- [Advertising status values](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisementstatus)
- [Microsoft-confirmed Stop status-reporting issue](https://learn.microsoft.com/en-us/answers/questions/578453/when-i-stop-the-advertising-on-my-bluetooth-le-ser)
