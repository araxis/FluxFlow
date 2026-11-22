using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Components.Bluetooth.Windows;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

internal static class NotificationChecks
{
    public static async Task CompetingSubscriptionsAsync(ILoggerFactory logs, BleSubscriptionMode firstMode)
    {
        using var companion = new WindowsBlePlatform(logs);
        var first = new BleClientOptions { DeviceAddress = "01:02:03:04:05:06", ServiceId = WindowsBle.ServiceId,
            CharacteristicId = WindowsBle.CharacteristicId, SubscriptionMode = firstMode };
        var other = first with { DeviceAddress = "010203040506",
            SubscriptionMode = firstMode == BleSubscriptionMode.Notify ? BleSubscriptionMode.Indicate : BleSubscriptionMode.Notify };
        const GattCharacteristicProperties properties = GattCharacteristicProperties.Notify | GattCharacteristicProperties.Indicate;
        using var stop = new CancellationTokenSource();
        var session = new GattNotificationSession(first, properties, stop.Token);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(5);
        var output = new BufferBlock<byte[]>();
        var owner = companion.WithSubscriptionAsync(first, async () =>
        {
            try { await session.DeliverAsync(() => { }, bytes => output.SendAsync(bytes)); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                session.Close();
                cleanupStarted.TrySetResult();
                await finishCleanup.Task;
            }
        });

        async Task ExpectRejectionAsync()
        {
            try { await companion.WithSubscriptionAsync(other, () => throw new Exception("Competing subscription reached the characteristic.")); }
            catch (InvalidOperationException error) when (error.Message.Contains("already has an active subscription", StringComparison.Ordinal)) { return; }
            throw new Exception("Competing subscription was accepted.");
        }

        try
        {
            await ExpectRejectionAsync();
            session.Receive(() => [0, 255, 128, 0]);
            if (Convert.ToHexString(await output.ReceiveAsync(timeout)) != "00FF8000")
                throw new Exception("Owner's bytes changed after contention.");
            stop.Cancel();
            await cleanupStarted.Task.WaitAsync(timeout);
            await ExpectRejectionAsync();
        }
        finally
        {
            stop.Cancel();
            finishCleanup.TrySetResult();
            await owner.WaitAsync(timeout);
        }

        var next = new GattNotificationSession(other, properties, CancellationToken.None);
        var freshOutput = new BufferBlock<byte[]>();
        await companion.WithSubscriptionAsync(other, async () =>
        {
            var delivery = next.DeliverAsync(() => { }, bytes => freshOutput.SendAsync(bytes));
            var staleRead = false;
            session.Receive(() => { staleRead = true; return [99]; });
            next.Receive(() => [128, 0, 255]);
            next.Close();
            await delivery.WaitAsync(timeout);
            if (staleRead) throw new Exception("A stopped callback read a value during the new subscription.");
        });
        if (Convert.ToHexString(await freshOutput.ReceiveAsync(timeout)) != "8000FF"
            || freshOutput.Count != 0 || output.Count != 0)
            throw new Exception("The new subscription lost, duplicated or changed a value.");
    }

    public static async Task ModesAndBytesAsync()
    {
        foreach (var (mode, property, descriptor) in new[] {
            (BleSubscriptionMode.Notify, GattCharacteristicProperties.Notify, GattClientCharacteristicConfigurationDescriptorValue.Notify),
            (BleSubscriptionMode.Indicate, GattCharacteristicProperties.Indicate, GattClientCharacteristicConfigurationDescriptorValue.Indicate) })
        {
            var options = new BleClientOptions { OutputCapacity = 2, SubscriptionMode = mode };
            var other = property == GattCharacteristicProperties.Notify ? GattCharacteristicProperties.Indicate : GattCharacteristicProperties.Notify;
            try { _ = new GattNotificationSession(options, other, default); throw new Exception("Incompatible mode accepted."); }
            catch (InvalidOperationException) { }
            var session = new GattNotificationSession(options, property, default);
            if (session.Descriptor != descriptor) throw new Exception("Incorrect client configuration descriptor.");
            session.Receive(() => [0, 255]); session.Receive(() => [128]);
            var values = new List<string>();
            await session.DeliverAsync(() => { }, bytes =>
            {
                values.Add(Convert.ToHexString(bytes));
                if (values.Count == 2) session.Close();
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(3));
            if (!values.SequenceEqual(["00FF", "80"])) throw new Exception("Accepted values were lost, changed or reordered.");
            var copied = false;
            session.Receive(() => { copied = true; return [42]; });
            if (copied) throw new Exception("A closed session read a stale value.");
        }
    }

    public static async Task StartupDisconnectAsync()
    {
        var session = new GattNotificationSession(new(), GattCharacteristicProperties.Notify, default);
        session.Receive(() => [1]); session.ObserveConnection(() => true);
        var ready = false; var delivered = false;
        try { await session.DeliverAsync(() => ready = true, _ => { delivered = true; return Task.CompletedTask; }); }
        catch (InvalidOperationException error) when (error.Message.Contains("disconnected", StringComparison.Ordinal))
        {
            if (ready || delivered) throw new Exception("Reported readiness or delivered data after a startup disconnect.");
            return;
        }
        throw new Exception("Startup disconnect was lost.");
    }

    public static async Task StopFirstAsync()
    {
        using var stop = new CancellationTokenSource();
        var session = new GattNotificationSession(new(), GattCharacteristicProperties.Notify, stop.Token);
        var delivery = session.DeliverAsync(() => { }, _ => throw new Exception("Delivered a stale notification."));
        stop.Cancel();
        session.ObserveConnection(() => throw new Exception("Read device state after Stop."));
        var copied = false;
        session.Receive(() => { copied = true; return [99]; });
        if (copied) throw new Exception("Read a value after Stop.");
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        throw new Exception("Stop did not cancel delivery.");
    }

    public static async Task DeliveryFailureAsync(bool overflow, bool stop)
    {
        using var lifetime = new CancellationTokenSource();
        var session = new GattNotificationSession(new() { OutputCapacity = 1 }, GattCharacteristicProperties.Notify, lifetime.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Receive(() => [1]);
        var values = new List<byte>();
        var delivery = session.DeliverAsync(() => { }, bytes => { values.Add(bytes.Single()); entered.TrySetResult(); return release.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            session.Receive(() => [2]); // Reader is busy; one slot remains in the callback queue.
            if (overflow) session.Receive(() => [3]); else session.ObserveConnection(() => true);
            if (stop) lifetime.Cancel();
        }
        finally { release.TrySetResult(); }
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (InvalidOperationException error) when (error.Message.Contains(overflow ? "buffer is full" : "disconnected", StringComparison.Ordinal))
        {
            if (!values.SequenceEqual(stop ? new byte[] { 1 } : [1, 2])) throw new Exception("Unexpected buffered delivery.");
            return;
        }
        throw new Exception("Subscription ended without reporting the failure.");
    }
}
