using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Bluetooth.Tests;

public sealed class BleGattModelTests
{
    [Fact]
    public void Standard_templates_use_distinct_primary_services_and_valid_values()
    {
        var battery = BleStandards.BatteryTemplate(true);
        var power = BleStandards.TxPowerTemplate(-20);
        new BleServerOptions { Services = [battery, power] }.Validate();
        power.Id.ShouldBe(BleStandards.Uuid(0x1804));
        var value = power.Characteristics.Single();
        value.Id.ShouldBe(BleStandards.Uuid(0x2a07));
        value.Properties.ShouldBe(BleCharacteristicProperties.Read);
        value.ReadProtection.ShouldBe(BleAccessProtection.Plain);
        value.GetBytes().ShouldBe(new byte[] { 236 });
        Should.Throw<ArgumentException>(() => BleStandards.TxPowerTemplate(21));
        battery.Characteristics[0].GetBytes().ShouldBe(new byte[] { 100 });
        BleStandards.Search("1804", BleUuidKind.Service).Single().Id.ShouldBe(power.Id);
    }

    [Fact]
    public void Multi_service_snapshot_preserves_legacy_values_and_is_detached()
    {
        var first = BleStandards.BatteryTemplate();
        var second = new BleService { Id = Guid.NewGuid(), Characteristics = [new() { Id = Guid.NewGuid(), Value = "legacy" }] };
        var options = new BleServerOptions { Services = [first, second] };
        options.Validate();
        var snapshot = options.Snapshot();
        options.Services![1].Characteristics[0] = new() { Id = Guid.NewGuid(), Value = "changed" };
        snapshot.GetServices()[1].Characteristics[0].GetBytes().ShouldBe(System.Text.Encoding.UTF8.GetBytes("legacy"));
        Should.Throw<ArgumentException>(() => (options with { Services = [first, first] }).Validate());
        Should.Throw<ArgumentException>(() => (options with { Services = [] }).Validate());
        Should.Throw<ArgumentException>(() => (options with { Services = [first with { Id = BleStandards.Uuid(0x180a) }] }).Validate());
    }

    [Fact]
    public void Standard_codecs_validate_exact_formats_without_mutating_original_bytes()
    {
        var bytes = new byte[] { 100 };
        BleStandards.Decode(BleStandards.Uuid(0x2a19), bytes).ShouldBe("100");
        BleStandards.Encode(BleStandards.Uuid(0x2a19), "42").Decode().ShouldBe(new byte[] { 42 });
        BleStandards.Encode(BleStandards.Uuid(0x2a07), "-20").Decode().ShouldBe(new byte[] { 236 });
        Should.Throw<ArgumentException>(() => BleStandards.Decode(BleStandards.Uuid(0x2a19), [101]));
        Should.Throw<ArgumentException>(() => BleStandards.Decode(BleStandards.Uuid(0x2a19), [42, 0]));
        Should.Throw<ArgumentException>(() => BleStandards.Decode(Guid.NewGuid(), bytes));
        bytes.ShouldBe(new byte[] { 100 });
        for (var i = 0; i < 100; i++) BleStandards.IsBluetoothUuid(BleStandards.NewCustomUuid()).ShouldBeFalse();
    }

    [Fact]
    public async Task Closing_a_session_rejects_a_late_notification_result_without_changing_a_fresh_session()
    {
        var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
        var options = new BleServerOptions { ServiceId = service, Characteristics = [new() { Id = characteristic,
            Properties = BleCharacteristicProperties.Read | BleCharacteristicProperties.Notify, InitialValue = new() { Data = "FE" } }] };
        var old = new BleServerSession(options, _ => Task.CompletedTask, CancellationToken.None);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.SetNotifier((_, _, _) => release.Task);
        var updating = old.UpdateAsync(new() { ServiceId = service, CharacteristicId = characteristic, Notify = true, Value = new() { Data = "00FF" } });
        updating.IsCompleted.ShouldBeFalse();
        old.Close();
        var fresh = new BleServerSession(options, _ => Task.CompletedTask, CancellationToken.None);
        release.SetResult();
        await Should.ThrowAsync<OperationCanceledException>(() => updating.WaitAsync(TimeSpan.FromSeconds(3)));
        Should.Throw<OperationCanceledException>(() => old.Read(service, characteristic));
        fresh.Read(service, characteristic).ShouldBe(new byte[] { 254 });
    }

    [Fact]
    public async Task Writes_address_the_characteristic_within_its_service_and_leave_read_only_values_untouched()
    {
        var characteristic = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var options = new BleServerOptions { Services = [
            new() { Id = first, Characteristics = [new() { Id = characteristic, InitialValue = new() { Data = "FF" } }] },
            new() { Id = second, Characteristics = [new() { Id = characteristic,
                Properties = BleCharacteristicProperties.Read | BleCharacteristicProperties.WriteWithoutResponse }] }] };
        options.Validate();
        var writes = new List<BleServerValue>();
        var session = new BleServerSession(options, value => { writes.Add(value); return Task.CompletedTask; }, default);
        await session.ReceiveWriteAsync(second, characteristic, [0, 128]);
        var copy = session.Read(second, characteristic); copy[0] = 99;
        await Should.ThrowAsync<InvalidOperationException>(() => session.ReceiveWriteAsync(first, characteristic, [33]));
        await Should.ThrowAsync<ArgumentException>(() => session.ReceiveWriteAsync(second, characteristic, new byte[513]));
        session.Read(first, characteristic).ShouldBe(new byte[] { 255 });
        session.Read(second, characteristic).ShouldBe(new byte[] { 0, 128 });
        var written = writes.Single();
        written.ServiceId.ShouldBe(second); written.CharacteristicId.ShouldBe(characteristic);
        written.ValueBase64.ShouldBe("AIA=");
    }

    [Fact]
    public async Task Server_updates_and_remote_writes_are_run_local_and_keep_original_bytes()
    {
        var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
        var options = new BleServerOptions { Services = [new() { Id = service, Characteristics = [new() { Id = characteristic,
            Properties = BleCharacteristicProperties.Read | BleCharacteristicProperties.Write | BleCharacteristicProperties.Notify,
            InitialValue = new() { Data = "00FF" } }] }] };
        var writes = new List<BleServerValue>();
        using var cancellation = new CancellationTokenSource();
        var session = new BleServerSession(options, value => { writes.Add(value); return Task.CompletedTask; }, cancellation.Token);
        byte[]? notified = null;
        session.SetNotifier((_, _, bytes) => { notified = bytes.ToArray(); return Task.CompletedTask; });
        await session.UpdateAsync(new() { ServiceId = service, CharacteristicId = characteristic, Notify = true, Value = new() { Encoding = BleValueEncoding.Base64, Data = "AQID" } });
        notified.ShouldBe(new byte[] { 1, 2, 3 });
        var incoming = new byte[] { 0, 255, 42 };
        await session.ReceiveWriteAsync(service, characteristic, incoming);
        incoming[0] = 100;
        session.Read(service, characteristic).ShouldBe(new byte[] { 0, 255, 42 });
        writes.Single().ValueBase64.ShouldBe("AP8q");
        options.GetServices()[0].Characteristics[0].GetBytes().ShouldBe(new byte[] { 0, 255 });
        cancellation.Cancel(); session.Close();
        await Should.ThrowAsync<OperationCanceledException>(() => session.ReceiveWriteAsync(service, characteristic, [4]));
        writes.Count.ShouldBe(1);
        var rerun = new BleServerSession(options, _ => Task.CompletedTask, CancellationToken.None);
        rerun.Read(service, characteristic).ShouldBe(new byte[] { 0, 255 });
    }
}
