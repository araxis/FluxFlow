using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace FluxFlow.Components.Bluetooth.Windows;

internal interface IGattService
{
    Task<GattStatus> Interrupted { get; }
    GattServiceProviderAdvertisementStatus Status { get; }
    Task ConfigureAsync(BleService definition, BleServerSession session, GattRequestLifetime requests, CancellationToken token);
    Task StartAsync(BleServerOptions options, CancellationToken token);
    Task NotifyAsync(Guid characteristic, byte[] bytes, CancellationToken token);
    void DetachRequests();
    Task StopAsync();
}

internal sealed class WindowsGattService : IGattService
{
    private readonly GattServiceProvider _provider;
    private readonly GattSessionSignals _signals;
    private readonly List<Action> _detach = [];
    private readonly Dictionary<Guid, GattLocalCharacteristic> _characteristics = [];
    private bool _requested;
    public Task<GattStatus> Interrupted => _signals.Interrupted.Task;
    public GattServiceProviderAdvertisementStatus Status => _provider.AdvertisementStatus;
    private WindowsGattService(GattServiceProvider provider, ILogger logger)
    {
        _provider = provider;
        _signals = new(logger);
        provider.AdvertisementStatusChanged += Changed;
    }
    private void Changed(GattServiceProvider _, GattServiceProviderAdvertisementStatusChangedEventArgs args) => _signals.Observe(args.Status, args.Error);
    public static async Task<WindowsGattService> CreateAsync(Guid id, ILogger logger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await GattServiceProvider.CreateAsync(id);
        if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"Create GATT service {id}: {result.Error}.");
        // Return ownership before checking cancellation, so rollback includes a late provider.
        return new(result.ServiceProvider, logger);
    }
    public async Task ConfigureAsync(BleService definition, BleServerSession session, GattRequestLifetime requests, CancellationToken token)
    {
        foreach (var item in definition.Characteristics)
        {
            token.ThrowIfCancellationRequested();
            var parameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = (GattCharacteristicProperties)(int)item.Properties,
                ReadProtectionLevel = (GattProtectionLevel)item.ReadProtection,
                WriteProtectionLevel = (GattProtectionLevel)item.WriteProtection,
                UserDescription = item.UserDescription
            };
            var result = await _provider.Service.CreateCharacteristicAsync(item.Id, parameters);
            if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"Create GATT characteristic {item.Id}: {result.Error}.");
            var characteristic = result.Characteristic;
            _characteristics.Add(item.Id, characteristic);
            void Read(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
            {
                var deferral = args.GetDeferral();
                try
                {
                    _ = requests.RunAsync(async () =>
                    {
                        try
                        {
                            var request = await args.GetRequestAsync();
                            if (request is null) return;
                            if (token.IsCancellationRequested) { request.RespondWithProtocolError(0x0e); return; }
                            var bytes = session.Read(definition.Id, item.Id);
                            if (request.Offset > bytes.Length) request.RespondWithProtocolError(0x07);
                            else request.RespondWithValue(bytes.AsSpan((int)request.Offset, Math.Min(bytes.Length - (int)request.Offset, (int)request.Length)).ToArray().AsBuffer());
                        }
                        finally { deferral.Complete(); }
                    });
                }
                catch (Exception) { deferral.Complete(); }
            }
            void Write(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
            {
                var deferral = args.GetDeferral();
                try
                {
                    _ = requests.RunAsync(async () =>
                    {
                        try
                        {
                            var request = await args.GetRequestAsync();
                            if (request is null) return;
                            if (token.IsCancellationRequested) { if (request.Option == GattWriteOption.WriteWithResponse) request.RespondWithProtocolError(0x0e); return; }
                            if (request.Offset != 0 || request.Value.Length > 512)
                            {
                                if (request.Option == GattWriteOption.WriteWithResponse) request.RespondWithProtocolError(request.Offset != 0 ? (byte)0x07 : (byte)0x0d);
                                return;
                            }
                            using var reader = DataReader.FromBuffer(request.Value);
                            var bytes = new byte[request.Value.Length];
                            reader.ReadBytes(bytes);
                            try { if (BleStandards.HasCodec(item.Id)) _ = BleStandards.Decode(item.Id, bytes); }
                            catch (ArgumentException)
                            {
                                if (request.Option == GattWriteOption.WriteWithResponse) request.RespondWithProtocolError(0x13);
                                return;
                            }
                            await session.ReceiveWriteAsync(definition.Id, item.Id, bytes).ConfigureAwait(false);
                            if (!token.IsCancellationRequested && request.Option == GattWriteOption.WriteWithResponse) request.Respond();
                        }
                        finally { deferral.Complete(); }
                    });
                }
                catch (Exception) { deferral.Complete(); }
            }
            if (item.Properties.HasFlag(BleCharacteristicProperties.Read))
            {
                characteristic.ReadRequested += Read;
                _detach.Add(() => characteristic.ReadRequested -= Read);
            }
            if ((item.Properties & (BleCharacteristicProperties.Write | BleCharacteristicProperties.WriteWithoutResponse)) != 0)
            {
                characteristic.WriteRequested += Write;
                _detach.Add(() => characteristic.WriteRequested -= Write);
            }
            foreach (var descriptor in item.Descriptors)
            {
                token.ThrowIfCancellationRequested();
                var created = await characteristic.CreateDescriptorAsync(descriptor.Id, new GattLocalDescriptorParameters
                {
                    StaticValue = descriptor.Value.Decode().AsBuffer(), ReadProtectionLevel = (GattProtectionLevel)descriptor.ReadProtection
                });
                if (created.Error != BluetoothError.Success) throw new InvalidOperationException($"Create GATT descriptor {descriptor.Id}: {created.Error}.");
            }
        }
    }
    public async Task StartAsync(BleServerOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _requested = true;
        _provider.StartAdvertising(new() { IsConnectable = options.IsConnectable, IsDiscoverable = options.IsDiscoverable });
        var status = await _signals.Startup.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!status.IsAdvertising) throw new InvalidOperationException($"GATT advertising {status.Status}; Windows error: {status.Error}.");
    }
    public async Task NotifyAsync(Guid characteristic, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var target = _characteristics[characteristic];
        var clients = target.SubscribedClients.ToArray();
        if (clients.Any(client => bytes.Length > client.MaxNotificationSize))
            throw new ArgumentException("The value exceeds a subscribed client's maximum notification size. No automatic truncation or fragmentation is performed.");
        foreach (var client in clients)
        {
            token.ThrowIfCancellationRequested();
            var result = await target.NotifyValueAsync(bytes.AsBuffer(), client);
            token.ThrowIfCancellationRequested();
            if (result.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"GATT notification failed: {result.Status}.");
        }
    }
    public void DetachRequests()
    {
        List<Exception>? errors = null;
        foreach (var detach in _detach)
        {
            try { detach(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        _detach.Clear();
        if (errors is not null) throw new AggregateException("Could not detach GATT request handlers.", errors);
    }
    public async Task StopAsync()
    {
        try
        {
            if (_requested && Status != GattServiceProviderAdvertisementStatus.Stopped)
            {
                _signals.BeginStop();
                _provider.StopAdvertising();
                if (Status != GattServiceProviderAdvertisementStatus.Stopped)
                {
                    try { await _signals.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                    catch (TimeoutException error)
                    {
                        if (Status != GattServiceProviderAdvertisementStatus.Stopped)
                            throw new TimeoutException($"Windows did not confirm GATT Stopped; last status: {Status}. Native cleanup is unverified.", error);
                    }
                }
            }
        }
        finally
        {
            _signals.Dispose();
            _provider.AdvertisementStatusChanged -= Changed;
        }
    }
}
