using System.Globalization;
using System.Runtime.InteropServices;
using FluxFlow.Components.Bluetooth.Windows;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;

// Parse before opening the host or touching the radio.
ulong? peerAddress = null;
var peerType = BluetoothAddressType.Public;
Guid peerService = default, peerCharacteristic = default;
int? serveSeconds = null;
var noHardware = args is ["--no-hardware"];
if (args.Length == 5 && args[0] == "--read" && args[2] is "public" or "random"
    && args[1].Length == 12 && ulong.TryParse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedAddress)
    && parsedAddress != 0 && Guid.TryParse(args[3], out peerService) && Guid.TryParse(args[4], out peerCharacteristic))
{
    peerAddress = parsedAddress;
    peerType = args[2] == "public" ? BluetoothAddressType.Public : BluetoothAddressType.Random;
}
else if (args.Length == 2 && args[0] == "--serve" && int.TryParse(args[1], out var seconds) && seconds is >= 1 and <= 120)
    serveSeconds = seconds;
else if (args.Length != 0 && !noHardware)
{
    Console.Error.WriteLine("Usage: [--no-hardware | --serve <1-120 seconds> | --read <12-hex-address> <public|random> <service-uuid> <characteristic-uuid>]");
    return 1;
}

var builder = Host.CreateApplicationBuilder();
builder.Services.AddLocalFlowRunner();
using var host = builder.Build();
await host.StartAsync();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FluxFlow.BleProof");
using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
var failures = 0;
var blocked = 0;
try
{
    log.LogInformation("Runtime: {Runtime}; OS: {OS}", RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription);
    await Check("Multi-service failure rolls back every acquired provider", async () =>
    {
        var created = new List<ProbeGattService>();
        var rollback = new List<int>();
        var server = new WindowsBle(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<WindowsBle>(), (_, _) =>
        {
            var provider = new ProbeGattService(created.Count, rollback) { FailStart = created.Count == 1 };
            created.Add(provider); return Task.FromResult<IGattService>(provider);
        });
        var options = new BleServerOptions { Services = Enumerable.Range(0, 3).Select(_ => new BleService { Id = Guid.NewGuid(),
            Characteristics = [new() { Id = Guid.NewGuid(), Value = "test" }] }).ToArray() };
        var started = false;
        try { await server.ServeAsync(lifetime.Token, options: options, started: () => started = true); throw new Exception("Expected startup failure."); }
        catch (ProofStartupException) { }
        Require(!started && server.ServerStopped && rollback.SequenceEqual(new[] { 2, 1, 0 }), "Rollback missed a provider or claimed readiness.");
        foreach (var provider in created) Require(provider.Detached && provider.Stops == 1, "Handlers or provider were not cleaned up.");
    });
    await Check("Cancellation owns late services and a clean server can rerun", async () =>
    {
        var created = new List<ProbeGattService>();
        var rollback = new List<int>();
        var acquiring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new WindowsBle(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<WindowsBle>(), async (_, _) =>
        {
            var provider = new ProbeGattService(created.Count, rollback);
            created.Add(provider);
            if (created.Count == 2) { acquiring.SetResult(); await release.Task; }
            return provider;
        });
        var options = new BleServerOptions { Services = [BleStandards.BatteryTemplate(), BleStandards.TxPowerTemplate()] };
        using var stop = new CancellationTokenSource();
        var started = false;
        var pending = server.ServeAsync(stop.Token, options: options, started: () => started = true);
        await acquiring.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        Require(!pending.IsCompleted, "Late native ownership was abandoned.");
        release.SetResult();
        await ExpectCancellation(pending);
        Require(!started && server.ServerStopped && rollback.SequenceEqual(new[] { 1, 0 }), "Cancelled startup missed cleanup.");
        using var rerun = new CancellationTokenSource();
        await ExpectCancellation(server.ServeAsync(rerun.Token, options: options, started: rerun.Cancel));
        Require(server.ServerStarts == 1 && server.ServerStopped && created.Count == 4, "Clean rerun did not acquire fresh services.");
        Require(created.All(provider => provider.Detached && provider.Stops == 1), "A service was leaked or stopped twice.");
    });
    await Check("Rollback attempts every provider and preserves startup and cleanup failures", async () =>
    {
        var created = new List<ProbeGattService>();
        var rollback = new List<int>();
        var server = new WindowsBle(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<WindowsBle>(), (_, _) =>
        {
            var provider = new ProbeGattService(created.Count, rollback) { FailStart = created.Count == 2, FailStop = created.Count == 1 };
            created.Add(provider); return Task.FromResult<IGattService>(provider);
        });
        var options = new BleServerOptions { Services = Enumerable.Range(0, 3).Select(_ => new BleService { Id = Guid.NewGuid(),
            Characteristics = [new() { Id = Guid.NewGuid() }] }).ToArray() };
        try { await server.ServeAsync(lifetime.Token, options: options); throw new Exception("Expected failure."); }
        catch (AggregateException error)
        {
            var failures = error.Flatten().InnerExceptions;
            Require(failures.Any(error => error is ProofStartupException) && failures.Any(error => error is TimeoutException), "Original failure was lost.");
        }
        Require(!server.ServerStopped && rollback.SequenceEqual(new[] { 2, 1, 0 }), "Rollback stopped after the first cleanup failure.");
    });
    await Check("Outstanding GATT requests drain and late requests are rejected", async () =>
    {
        var requests = new GattRequestLifetime(1, CancellationToken.None);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = requests.RunAsync(() => completion.Task);
        var full = false;
        try { _ = requests.RunAsync(() => Task.CompletedTask); } catch (InvalidOperationException) { full = true; }
        Require(full && requests.Failed.Task.IsCompleted, "Unbounded GATT requests accepted.");
        var draining = requests.CloseAsync();
        Require(!draining.IsCompleted, "Outstanding request abandoned.");
        completion.SetResult(); await pending; await draining;
        await ExpectCancellation(Task.Run(() => requests.RunAsync(() => Task.CompletedTask)));
    });
    await Check("Completed requests immediately free capacity for the next GATT request", async () =>
    {
        var requests = new GattRequestLifetime(1, CancellationToken.None);
        for (var i = 0; i < 128; i++)
            await requests.RunAsync(() => Task.CompletedTask);
        await requests.CloseAsync();
        Require(!requests.Failed.Task.IsCompleted, "Sequential requests exhausted the GATT request buffer.");
    });
    await Check("An interrupted service cannot announce multi-service readiness", async () =>
    {
        var rollback = new List<int>();
        var first = new ProbeGattService(0, rollback);
        var second = new ProbeGattService(1, rollback) { OnStart = first.Interrupt };
        var options = new BleServerOptions { Services = [BleStandards.BatteryTemplate(), BleStandards.TxPowerTemplate()] };
        var server = new WindowsBle(host.Services.GetRequiredService<ILogger<WindowsBle>>(),
            (id, _) => Task.FromResult<IGattService>(id == options.Services[0].Id ? first : second));
        var ready = false;
        try { await server.ServeAsync(default, started: () => ready = true, options: options); throw new Exception("Expected advertising interruption."); }
        catch (InvalidOperationException error) when (error.Message.Contains("Advertising interrupted", StringComparison.Ordinal)) { }
        Require(!ready && server.ServerStarts == 0 && server.ServerStopped && rollback.SequenceEqual([1, 0]),
            "An interrupted server reported readiness or missed rollback.");
    });
    await Check("Detach failure during setup cannot skip rollback or allow an unsafe rerun", async () =>
    {
        var rollback = new List<int>();
        var options = new BleServerOptions { Services = [BleStandards.BatteryTemplate(), BleStandards.TxPowerTemplate()] };
        var count = 0;
        var server = new WindowsBle(host.Services.GetRequiredService<ILogger<WindowsBle>>(), (_, _) =>
        {
            var index = count++;
            return Task.FromResult<IGattService>(new ProbeGattService(index, rollback) { FailDetach = index == 0 });
        });
        try { await server.ServeAsync(default, failBeforeAdvertising: true, options: options); throw new Exception("Expected rollback failure."); }
        catch (AggregateException error)
        {
            var causes = error.Flatten().InnerExceptions;
            Require(causes.Any(e => e is ProofStartupException) && causes.Any(e => e.Message == "Detach failed."), "Lost startup or detach failure.");
        }
        Require(rollback.SequenceEqual([1, 0]) && !server.ServerStopped, "Detach failure skipped Stop or claimed clean shutdown.");
        try { await server.ServeAsync(default, options: options, failBeforeAdvertising: true); throw new Exception("Unsafe rerun accepted."); }
        catch (InvalidOperationException error) when (error.Message.Contains("unconfirmed", StringComparison.Ordinal)) { }
        Require(count == 2, "Acquired a new provider after failed cleanup.");
    });
    await Check("Multi-service unconfirmed Stop prevents rerun", async () =>
    {
        var rollback = new List<int>();
        var options = new BleServerOptions { Services = [BleStandards.BatteryTemplate(), BleStandards.TxPowerTemplate()] };
        var count = 0;
        var server = new WindowsBle(host.Services.GetRequiredService<ILogger<WindowsBle>>(), (_, _) =>
        {
            var index = count++;
            return Task.FromResult<IGattService>(new ProbeGattService(index, rollback) { FailStop = index == 1 });
        });
        using var stop = new CancellationTokenSource();
        try { await server.ServeAsync(stop.Token, started: stop.Cancel, options: options); throw new Exception("Expected unconfirmed Stop."); }
        catch (AggregateException error) when (error.Flatten().InnerExceptions.Any(e => e is TimeoutException)) { }
        Require(!server.ServerStopped && rollback.SequenceEqual([1, 0]), "Unconfirmed native Stop reported success or skipped a service.");
        try { await server.ServeAsync(default, options: options, failBeforeAdvertising: true); throw new Exception("Unsafe rerun accepted."); }
        catch (InvalidOperationException error) when (error.Message.Contains("unconfirmed", StringComparison.Ordinal)) { }
        Require(count == 2, "A new run acquired providers after unconfirmed Stop.");
    });
    await Check("Late native handle is disposed on cancellation", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<ProbeHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = OwnedNativeOperation.AcquireAsync(() => completion.Task, cancellation.Token);
        cancellation.Cancel();
        var handle = new ProbeHandle();
        completion.SetResult(handle);
        await ExpectCancellation(pending);
        Require(handle.DisposeCount == 1, "Late native handle was not disposed exactly once.");
    });
    await Check("Pre-cancelled acquisition does not call native API", async () =>
    {
        var called = false;
        await ExpectCancellation(OwnedNativeOperation.AcquireAsync(() => { called = true; return Task.FromResult(new ProbeHandle()); }, new CancellationToken(true)));
        Require(!called, "Native API called after cancellation.");
    });
    await Check("Successful acquisition transfers disposal to the caller", async () =>
    {
        var handle = new ProbeHandle();
        using (var acquired = await OwnedNativeOperation.AcquireAsync(() => Task.FromResult(handle), lifetime.Token))
            Require(ReferenceEquals(handle, acquired) && handle.DisposeCount == 0, "Ownership was not transferred.");
        Require(handle.DisposeCount == 1, "Caller did not dispose the handle exactly once.");
    });
    await Check("GATT startup and Stop signals are scoped to the current phase", () =>
    {
        using var signals = new GattSessionSignals(new CountingLogger());
        signals.Observe(GattServiceProviderAdvertisementStatus.Aborted, BluetoothError.Success);
        Require(!signals.Startup.Task.IsCompleted && !signals.Interrupted.Task.IsCompleted, "Interim event settled startup.");
        signals.Observe(GattServiceProviderAdvertisementStatus.Started, BluetoothError.Success);
        Require(signals.Startup.Task.Result.IsAdvertising && !signals.Interrupted.Task.IsCompleted, "Interim abort interrupted the started session.");
        signals.Observe(GattServiceProviderAdvertisementStatus.Stopped, BluetoothError.Success);
        signals.BeginStop();
        Require(!signals.Stopped.Task.IsCompleted, "A prior event confirmed a later Stop request.");
        signals.Observe(GattServiceProviderAdvertisementStatus.Aborted, BluetoothError.Success);
        Require(!signals.Stopped.Task.IsCompleted, "Aborted was accepted as confirmed Stop.");
        signals.Observe(GattServiceProviderAdvertisementStatus.Stopped, BluetoothError.Success);
        Require(signals.Stopped.Task.IsCompletedSuccessfully, "Current Stop was not confirmed.");
        return Task.CompletedTask;
    });
    await Check("Closed GATT sessions reject queued events and logs", () =>
    {
        var counter = new CountingLogger();
        using var previous = new GattSessionSignals(counter);
        using var current = new GattSessionSignals(counter);
        previous.BeginStop();
        previous.Dispose();
        Parallel.For(0, 100, _ =>
        {
            previous.Observe(GattServiceProviderAdvertisementStatus.Started, BluetoothError.Success);
            previous.Observe(GattServiceProviderAdvertisementStatus.Stopped, BluetoothError.Success);
        });
        Require(counter.Count == 0 && !previous.Startup.Task.IsCompleted && !previous.Stopped.Task.IsCompleted
            && !current.Startup.Task.IsCompleted, "A closed session accepted a late event.");
        return Task.CompletedTask;
    });
    await Check("Operation and cleanup failures both retain their original exceptions", async () =>
    {
        var original = new InvalidOperationException("Operation failure");
        var cleanup = new TimeoutException("Unconfirmed Stop");
        AggregateException? captured = null;
        try { await OwnedNativeOperation.RunAsync(() => Task.FromException(original), () => Task.FromException(cleanup)); }
        catch (AggregateException error) { captured = error; }
        Require(captured is { InnerExceptions.Count: 2 } && ReferenceEquals(captured.InnerExceptions[0], original)
            && ReferenceEquals(captured.InnerExceptions[1], cleanup), "One of the original exceptions was lost.");
    });
    await Check("Cancellation waits for cleanup and does not hide cleanup failure", async () =>
    {
        var cancelled = new OperationCanceledException(new CancellationToken(true));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = OwnedNativeOperation.RunAsync(() => Task.FromException(cancelled), () => completed.Task);
        Require(!pending.IsCompleted, "Cancellation skipped pending cleanup.");
        completed.SetResult();
        await ExpectCancellation(pending);
        AggregateException? captured = null;
        try { await OwnedNativeOperation.RunAsync(() => Task.FromException(cancelled), () => Task.FromException(new TimeoutException())); }
        catch (AggregateException error) { captured = error; }
        Require(captured?.InnerExceptions[0] == cancelled && captured.InnerExceptions[1] is TimeoutException,
            "Unconfirmed cleanup was reported as ordinary cancellation.");
    });
    await Check("Notification modes, bounded capacity and ordered bytes", NotificationChecks.ModesAndBytesAsync);
    foreach (var mode in new[] { BleSubscriptionMode.Notify, BleSubscriptionMode.Indicate })
        await Check($"{mode} holds the characteristic through Stop and isolates the next subscription",
            () => NotificationChecks.CompetingSubscriptionsAsync(host.Services.GetRequiredService<ILoggerFactory>(), mode));
    await Check("Startup disconnect does not report subscription readiness", NotificationChecks.StartupDisconnectAsync);
    await Check("Stop before late notification callbacks remains cancellation", NotificationChecks.StopFirstAsync);
    foreach (var stop in new[] { false, true })
    {
        await Check($"Notification overflow with Stop={stop}", () => NotificationChecks.DeliveryFailureAsync(true, stop));
        await Check($"Notification disconnect with Stop={stop}", () => NotificationChecks.DeliveryFailureAsync(false, stop));
    }
    var ble = new WindowsBle(host.Services.GetRequiredService<ILogger<WindowsBle>>());
    await Check("Pre-cancelled scan, server and client do not start", async () =>
    {
        var cancelled = new CancellationToken(true);
        await ExpectCancellation(ble.ScanAsync(cancelled));
        await ExpectCancellation(ble.ServeAsync(cancelled));
        await ExpectCancellation(WindowsBle.ReadAsync(0, BluetoothAddressType.Public, WindowsBle.ServiceId, WindowsBle.CharacteristicId, cancelled));
        var options = new FluxFlow.Components.Bluetooth.BleClientOptions { DeviceAddress = "010203040506", ServiceId = WindowsBle.ServiceId, CharacteristicId = WindowsBle.CharacteristicId };
        await ExpectCancellation(WindowsBle.WriteAsync(options, [1], FluxFlow.Components.Bluetooth.BleWriteMode.WithResponse, cancelled));
        await ExpectCancellation(WindowsBle.SubscribeAsync(options, _ => throw new InvalidOperationException("Late value"), () => throw new InvalidOperationException("Late start"), cancelled));
        Require(ble.ScanStarts == 0 && ble.ServerStarts == 0, "A pre-cancelled session started.");
    });
    var adapter = noHardware ? null : await BluetoothAdapter.GetDefaultAsync();
    if (noHardware) Block("Hardware checks explicitly skipped; deterministic cleanup checks do not verify radio delivery.");
    else if (adapter is null)
    {
        Block("No Windows Bluetooth adapter is available.");
    }
    else
    {
        var radio = await adapter.GetRadioAsync();
        log.LogInformation("Adapter: {Name}; radio: {State}; LE: {LE}; central: {Central}; peripheral: {Peripheral}",
            radio?.Name, radio?.State, adapter.IsLowEnergySupported, adapter.IsCentralRoleSupported, adapter.IsPeripheralRoleSupported);
        if (radio?.State != RadioState.On || !adapter.IsLowEnergySupported)
            Block("BLE radio is unavailable or off; its settings were not changed.");
        else
        {
            for (var attempt = 1; attempt <= 2 && !lifetime.IsCancellationRequested; attempt++)
            {
                await Check($"Scan {attempt}: cancellation, stop and no late records", async () =>
                {
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    stop.CancelAfter(TimeSpan.FromSeconds(3));
                    await ExpectCancellation(ble.ScanAsync(stop.Token));
                    Require(ble.ScanStopped, "Advertisement watcher did not stop.");
                    var count = ble.Advertisements;
                    await Task.Delay(250, lifetime.Token);
                    Require(ble.Advertisements == count, "Advertisements accepted after stop.");
                    log.LogInformation("Advertisements received (count only): {Count}", count);
                });
            }
            if (!adapter.IsPeripheralRoleSupported) Block("Adapter/driver does not support peripheral role; live GATT server publication cannot be verified.");
            else
            {
                await Check("Server startup failure leaves advertising stopped", async () =>
                {
                    try { await ble.ServeAsync(lifetime.Token, failBeforeAdvertising: true); throw new InvalidOperationException("Injected failure did not occur."); }
                    catch (ProofStartupException) { Require(ble.ServerStopped, "Startup cleanup did not finish."); }
                });
                for (var attempt = 1; attempt <= 2 && ble.ServerStopped && !lifetime.IsCancellationRequested; attempt++)
                {
                    await Check($"GATT server {attempt}: advertise, cancellation and stop", async () =>
                    {
                        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        stop.CancelAfter(TimeSpan.FromSeconds(15));
                        var started = false;
                        await ExpectCancellation(ble.ServeAsync(stop.Token, started: () => { started = true; stop.CancelAfter(TimeSpan.FromSeconds(2)); }));
                        Require(started && ble.ServerStopped, "Server did not advertise and stop successfully.");
                        log.LogInformation("Windows advertising status before Stop: {Status}", ble.ServerAdvertisingStatus);
                        if (ble.ServerAdvertisingStatus == Windows.Devices.Bluetooth.GenericAttributeProfile.GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
                            Block("Windows could not include all requested advertising data; peer discovery needs verification.");
                    });
                }
                if (!ble.ServerStopped)
                {
                    Block("Further GATT server attempts skipped because Stop was not confirmed; process exit is required.");
                    await Check("Unconfirmed Stop rejects another server invocation", async () =>
                    {
                        var starts = ble.ServerStarts;
                        var rejected = false;
                        try { await ble.ServeAsync(lifetime.Token); }
                        catch (InvalidOperationException) { rejected = true; }
                        Require(rejected && !ble.ServerStopped && ble.ServerStarts == starts, "Unconfirmed server was reused.");
                    });
                }
                if (ble.ServerStopped && serveSeconds is { } duration)
                    await Check("Extended GATT server session", async () =>
                    {
                        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        stop.CancelAfter(TimeSpan.FromSeconds(15));
                        var started = false;
                        await ExpectCancellation(ble.ServeAsync(stop.Token, started: () =>
                        {
                            started = true;
                            stop.CancelAfter(TimeSpan.FromSeconds(duration));
                            log.LogInformation("Read-only GATT server available for {Seconds}s. Service {Service}; characteristic {Characteristic}", duration, WindowsBle.ServiceId, WindowsBle.CharacteristicId);
                        }));
                        Require(started && ble.ServerStopped, "Extended session did not start and stop.");
                    });
            }
            if (peerAddress is { } address)
            {
                if (!adapter.IsCentralRoleSupported) Block("Adapter does not support central role.");
                else await Check("Selected peer: uncached discovery and read", async () =>
                {
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    stop.CancelAfter(TimeSpan.FromSeconds(20));
                    var bytes = await WindowsBle.ReadAsync(address, peerType, peerService, peerCharacteristic, stop.Token);
                    log.LogInformation("Read {Count} bytes; device and service handles disposed. Payload is not logged.", bytes.Length);
                });
            }
            else Block("GATT client discovery/read needs an explicitly selected independent test peer; no nearby device was connected.");
            Block("Remote reads of the local server require independent peer evidence; advertising alone does not prove delivery.");
        }
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
{
    Block("Proof interrupted by host shutdown.");
}
catch (Exception error)
{
    failures++;
    log.LogError(error, "Proof could not complete.");
}
finally
{
    await host.StopAsync(CancellationToken.None);
}
log.LogInformation("Proof complete: {Failures} failures; {Blocked} hardware/peer limitations.", failures, blocked);
return failures > 0 ? 1 : blocked > 0 ? 2 : 0;

async Task Check(string name, Func<Task> action)
{
    lifetime.Token.ThrowIfCancellationRequested();
    try { await action(); log.LogInformation("PASS: {Check}", name); }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
    catch (Exception error) { failures++; log.LogError(error, "FAIL: {Check}", name); }
}
void Block(string reason) { blocked++; log.LogWarning("NOT VERIFIED: {Reason}", reason); }
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task ExpectCancellation(Task operation)
{
    try { await operation; }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Expected cancellation.");
}
internal sealed class ProbeHandle : IDisposable
{
    public int DisposeCount { get; private set; }
    public void Dispose() => DisposeCount++;
}
internal sealed class ProbeGattService(int id, List<int> rollback) : IGattService
{
    private readonly TaskCompletionSource<GattStatus> _interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<GattStatus> Interrupted => _interrupted.Task;
    public GattServiceProviderAdvertisementStatus Status { get; private set; }
    public bool FailStart, FailStop, FailDetach, Detached;
    public Action? OnStart;
    public void Interrupt() => _interrupted.TrySetResult(new(GattServiceProviderAdvertisementStatus.Aborted, BluetoothError.RadioNotAvailable));
    public int Stops;
    public Task ConfigureAsync(BleService definition, BleServerSession session, GattRequestLifetime requests, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task StartAsync(BleServerOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (FailStart) throw new ProofStartupException();
        Status = GattServiceProviderAdvertisementStatus.Started; OnStart?.Invoke(); return Task.CompletedTask;
    }
    public Task NotifyAsync(Guid characteristic, byte[] bytes, CancellationToken token) => Task.CompletedTask;
    public void DetachRequests() { if (FailDetach) throw new InvalidOperationException("Detach failed."); Detached = true; }
    public Task StopAsync() { Stops++; rollback.Add(id); if (FailStop) throw new TimeoutException("Unconfirmed GATT Stop"); Status = GattServiceProviderAdvertisementStatus.Stopped; return Task.CompletedTask; }
}
internal sealed class CountingLogger : ILogger
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Interlocked.Increment(ref _count);
}
