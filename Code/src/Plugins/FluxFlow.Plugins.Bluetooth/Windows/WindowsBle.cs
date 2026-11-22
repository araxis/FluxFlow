using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace FluxFlow.Components.Bluetooth.Windows;

internal sealed class WindowsBle(ILogger<WindowsBle> logger, Func<Guid, CancellationToken, Task<IGattService>>? createService = null)
{
    public static readonly Guid ServiceId = new("3f63b38e-98df-4a52-a9f8-76b803a934b2");
    public static readonly Guid CharacteristicId = new("f50d7529-4e5d-4088-b88f-e236cd1e7f29");
    public const string Payload = "FluxFlow BLE proof";
    public int Advertisements { get; private set; }
    public int ScanStarts { get; private set; }
    public int ServerStarts { get; private set; }
    public bool ScanStopped { get; private set; } = true;
    public bool ServerStopped { get; private set; } = true;
    public GattServiceProviderAdvertisementStatus ServerAdvertisingStatus { get; private set; }

    public async Task ScanAsync(CancellationToken cancellation, Action<BleDeviceInfo>? received = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        var gate = new object();
        var accepting = true;
        var stopped = new TaskCompletionSource<BluetoothError>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            lock (gate)
            {
                if (accepting)
                {
                    Advertisements++;
                    if (args.BluetoothAddress != 0 && args.BluetoothAddressType is BluetoothAddressType.Public or BluetoothAddressType.Random)
                        received?.Invoke(new(args.BluetoothAddress.ToString("X12"),
                            args.BluetoothAddressType == BluetoothAddressType.Public ? BleAddressType.Public : BleAddressType.Random,
                            args.Advertisement.LocalName, args.RawSignalStrengthInDBm));
                }
            }
        }
        void Stopped(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementWatcherStoppedEventArgs args) => stopped.TrySetResult(args.Error);
        watcher.Received += Received;
        watcher.Stopped += Stopped;
        await OwnedNativeOperation.RunAsync(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            watcher.Start();
            ScanStarts++;
            ScanStopped = false;
            await stopped.Task.WaitAsync(cancellation);
            throw new InvalidOperationException($"Scan stopped unexpectedly: {await stopped.Task}.");
        }, async () =>
        {
            lock (gate) accepting = false;
            try
            {
                if (watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Stopping)
                {
                    watcher.Stop();
                    await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                ScanStopped = watcher.Status is BluetoothLEAdvertisementWatcherStatus.Stopped
                    or BluetoothLEAdvertisementWatcherStatus.Aborted or BluetoothLEAdvertisementWatcherStatus.Created;
            }
            finally
            {
                watcher.Received -= Received;
                watcher.Stopped -= Stopped;
            }
        });
    }

    public async Task ServeAsync(CancellationToken cancellation, bool failBeforeAdvertising = false, Action? started = null, BleServerOptions? options = null, BleServerSession? session = null)
    {
        cancellation.ThrowIfCancellationRequested();
        options ??= new() { ServiceId = ServiceId, Characteristics = [new() { Id = CharacteristicId, Value = Payload }] };
        options.Validate();
        if (!ServerStopped) throw new InvalidOperationException("Previous GATT Stop is unconfirmed; restart the host process before starting another server.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        session ??= new(options, _ => Task.CompletedTask, lifetime.Token);
        var requests = new GattRequestLifetime(options.OutputCapacity, lifetime.Token);
        var providers = new Dictionary<Guid, IGattService>();
        await OwnedNativeOperation.RunAsync(async () =>
        {
            foreach (var definition in options.GetServices())
            {
                var provider = createService is null
                    ? await WindowsGattService.CreateAsync(definition.Id, logger, lifetime.Token).ConfigureAwait(false)
                    : await createService(definition.Id, lifetime.Token).ConfigureAwait(false);
                providers.Add(definition.Id, provider);
                await provider.ConfigureAsync(definition, session, requests, lifetime.Token).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            if (failBeforeAdvertising) throw new ProofStartupException();
            ServerStopped = false;
            foreach (var provider in providers.Values) await provider.StartAsync(options, lifetime.Token).ConfigureAwait(false);
            var interrupted = Task.WhenAny(providers.Values.Select(provider => provider.Interrupted));
            var terminal = Task.WhenAny(interrupted, requests.Failed.Task);
            if (!terminal.IsCompleted)
            {
                cancellation.ThrowIfCancellationRequested();
                session.SetNotifier((service, characteristic, bytes) => requests.RunAsync(() => providers[service].NotifyAsync(characteristic, bytes, lifetime.Token)));
                ServerAdvertisingStatus = providers.Values.Last().Status;
                ServerStarts++;
                started?.Invoke();
            }
            var completed = await terminal.WaitAsync(cancellation).ConfigureAwait(false);
            if (completed == requests.Failed.Task) throw await requests.Failed.Task.ConfigureAwait(false);
            var interruption = await await interrupted.ConfigureAwait(false);
            throw new InvalidOperationException($"Advertising interrupted: {interruption.Status}; Windows error: {interruption.Error}.");
        }, async () =>
        {
            var errors = new List<Exception>();
            session.Close();
            try { lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
            foreach (var provider in providers.Values)
                try { provider.DetachRequests(); } catch (Exception error) { errors.Add(error); }
            try { await requests.CloseAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            foreach (var provider in providers.Values.Reverse())
                try { await provider.StopAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            ServerStopped = errors.Count == 0;
            if (errors.Count > 0) throw new AggregateException("GATT cleanup is unconfirmed. Restart the companion before another server run.", errors);
        });
    }

    public static async Task<byte[]> ReadAsync(ulong address, BluetoothAddressType addressType, Guid serviceId, Guid characteristicId, CancellationToken cancellation)
        => await WithCharacteristicAsync(address, addressType, serviceId, characteristicId, async (_, characteristic) =>
        {
            if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                throw new InvalidOperationException("Selected characteristic is not readable.");
            cancellation.ThrowIfCancellationRequested();
            var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            cancellation.ThrowIfCancellationRequested();
            RequireSuccess(read.Status, read.ProtocolError, "Read characteristic");
            return Bytes(read.Value);
        }, cancellation).ConfigureAwait(false);

    public static async Task WriteAsync(BleClientOptions options, byte[] value, BleWriteMode mode, CancellationToken cancellation)
        => await WithCharacteristicAsync(options.Address, AddressType(options), options.ServiceId, options.CharacteristicId, async (_, characteristic) =>
        {
            var required = mode == BleWriteMode.WithResponse ? GattCharacteristicProperties.Write : GattCharacteristicProperties.WriteWithoutResponse;
            if (!characteristic.CharacteristicProperties.HasFlag(required))
                throw new InvalidOperationException($"Selected characteristic does not support {mode} writes.");
            cancellation.ThrowIfCancellationRequested();
            var result = await characteristic.WriteValueWithResultAsync(value.AsBuffer(), mode == BleWriteMode.WithResponse
                ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse);
            cancellation.ThrowIfCancellationRequested();
            RequireSuccess(result.Status, result.ProtocolError, "Write characteristic");
            return true;
        }, cancellation).ConfigureAwait(false);

    public static async Task SubscribeAsync(BleClientOptions options, Func<byte[], Task> received, Action started, CancellationToken cancellation)
        => await WithCharacteristicAsync(options.Address, AddressType(options), options.ServiceId, options.CharacteristicId, async (device, characteristic) =>
        {
            var session = new GattNotificationSession(options, characteristic.CharacteristicProperties, cancellation);
            var requested = false;
            void Changed(GattCharacteristic _, GattValueChangedEventArgs args)
                => session.Receive(() => Bytes(args.CharacteristicValue));
            void ConnectionChanged(BluetoothLEDevice sender, object _)
                => session.ObserveConnection(() => sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected);
            characteristic.ValueChanged += Changed;
            device.ConnectionStatusChanged += ConnectionChanged;
            await OwnedNativeOperation.RunAsync(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                requested = true;
                var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(session.Descriptor);
                await session.DeliverAsync(() =>
                {
                    RequireSuccess(result.Status, result.ProtocolError, "Subscribe to characteristic");
                    started();
                }, received).ConfigureAwait(false);
            }, async () =>
            {
                session.Close();
                characteristic.ValueChanged -= Changed;
                device.ConnectionStatusChanged -= ConnectionChanged;
                // Await the native result, even after cancellation, before releasing service handles.
                if (requested)
                {
                    var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    RequireSuccess(result.Status, result.ProtocolError, "Unsubscribe from characteristic");
                }
            }).ConfigureAwait(false);
            return true;
        }, cancellation).ConfigureAwait(false);

    private static BluetoothAddressType AddressType(BleClientOptions options) => options.AddressType == BleAddressType.Public ? BluetoothAddressType.Public : BluetoothAddressType.Random;
    private static byte[] Bytes(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<T> WithCharacteristicAsync<T>(ulong address, BluetoothAddressType addressType, Guid serviceId, Guid characteristicId,
        Func<BluetoothLEDevice, GattCharacteristic, Task<T>> operation, CancellationToken cancellation)
    {
        using var device = await OwnedNativeOperation.AcquireAsync(
            () => BluetoothLEDevice.FromBluetoothAddressAsync(address, addressType).AsTask(), cancellation);
        if (device is null) throw new InvalidOperationException("Windows did not grant access to the selected BLE device.");
        cancellation.ThrowIfCancellationRequested();
        // Do not abandon service discovery on cancellation: late service handles also need disposal.
        // Some peripherals support full discovery but reject Windows' targeted service query.
        var discovery = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            RequireSuccess(discovery.Status, discovery.ProtocolError, "Discover service");
            var matches = discovery.Services.Where(service => service.Uuid == serviceId).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one matching GATT service.");
            var service = matches[0];
            var opened = await service.OpenAsync(GattSharingMode.SharedReadAndWrite);
            cancellation.ThrowIfCancellationRequested();
            if (opened is not (GattOpenStatus.Success or GattOpenStatus.AlreadyOpened))
                throw new InvalidOperationException($"Open GATT service for shared access: {opened}.");
            var characteristics = await service.GetCharacteristicsForUuidAsync(characteristicId, BluetoothCacheMode.Uncached);
            cancellation.ThrowIfCancellationRequested();
            RequireSuccess(characteristics.Status, characteristics.ProtocolError, "Discover characteristic");
            if (characteristics.Characteristics.Count != 1) throw new InvalidOperationException("Expected exactly one matching GATT characteristic.");
            return await operation(device, characteristics.Characteristics[0]).ConfigureAwait(false);
        }
        finally
        {
            foreach (var service in discovery.Services) service.Dispose();
        }
    }

    public static async Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions options, CancellationToken cancellation)
    {
        using var device = await OwnedNativeOperation.AcquireAsync(() => BluetoothLEDevice.FromBluetoothAddressAsync(options.Address,
            options.AddressType == BleAddressType.Public ? BluetoothAddressType.Public : BluetoothAddressType.Random).AsTask(), cancellation);
        if (device is null) throw new InvalidOperationException("Windows did not grant access to the selected BLE device.");
        cancellation.ThrowIfCancellationRequested();
        var discovery = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            RequireSuccess(discovery.Status, discovery.ProtocolError, "Discover services");
            if (discovery.Services.Count > 64) throw new InvalidOperationException("The device exposes more than 64 services. Enter the target UUIDs manually.");
            var services = new List<BleServiceInfo>();
            foreach (var service in discovery.Services)
            {
                cancellation.ThrowIfCancellationRequested();
                var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                cancellation.ThrowIfCancellationRequested();
                RequireSuccess(result.Status, result.ProtocolError, "Discover characteristics");
                if (result.Characteristics.Count > 256) throw new InvalidOperationException("The service exposes more than 256 characteristics. Enter the target UUIDs manually.");
                services.Add(new(service.Uuid, result.Characteristics.Select(value => new BleCharacteristicInfo(value.Uuid,
                    value.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read), value.CharacteristicProperties.ToString())).ToArray()));
            }
            return services.ToArray();
        }
        finally { foreach (var service in discovery.Services) service.Dispose(); }
    }

    private static void RequireSuccess(GattCommunicationStatus status, byte? protocolError, string operation)
    {
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"{operation}: {status}; ATT error: {protocolError}.");
    }
}

internal sealed class ProofStartupException : Exception;
