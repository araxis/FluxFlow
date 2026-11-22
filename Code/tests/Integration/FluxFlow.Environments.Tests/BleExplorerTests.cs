using FluxFlow.Plugins.Bluetooth.Design;
using System.Net;
using System.Net.Http.Json;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BleExplorerTests
{
    [Fact]
    public async Task Explorer_roundtrips_device_types_services_and_original_bytes_through_companion()
    {
        var backend = new Explorer();
        await using var app = await Start(backend);
        using var http = app.GetTestClient();
        var explorer = new HttpBleExplorer(http);
        (await explorer.ScanAsync(default)).Single().AddressType.ShouldBe(BleAddressType.Random);
        var options = Options();
        var service = (await explorer.DiscoverAsync(options, default)).Single();
        service.Id.ShouldBe(options.ServiceId); service.Characteristics.Single().Readable.ShouldBeTrue();
        (await explorer.ReadAsync(options, default)).ShouldBe(new byte[] { 0, 255, 42 });
        backend.Reads.ShouldBe(1);
    }
    [Fact]
    public async Task Protected_routes_and_validation_reject_requests_before_touching_hardware()
    {
        var backend = new Explorer();
        await using var app = await Start(backend);
        using var http = app.GetTestClient();
        using var denied = await http.PostAsync("/api/runs/bluetooth/scan", null);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden); backend.Scans.ShouldBe(0);
        http.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        using var invalid = await http.PostAsync("/api/runs/bluetooth/read", new StringContent("{\"DeviceAddress\":\"bad\"}", System.Text.Encoding.UTF8, "application/json"));
        var result = await invalid.Content.ReadFromJsonAsync(BleExplorerJsonContext.Default.BleExploreResult);
        result!.Error.ShouldNotBeNullOrWhiteSpace(); backend.Reads.ShouldBe(0);
        http.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var foreign = await http.PostAsync("/api/runs/bluetooth/scan", null);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden); backend.Scans.ShouldBe(0);
    }
    [Fact]
    public async Task Missing_native_backend_is_actionable_and_cancellation_releases_pending_operation()
    {
        await using (var unsupported = await Start(null))
        {
            using var http = unsupported.GetTestClient();
            var error = await Should.ThrowAsync<InvalidOperationException>(() => new HttpBleExplorer(http).ScanAsync(default));
            error.Message.ShouldContain("Manual configuration");
        }
        var backend = new Explorer { Wait = true };
        await using var app = await Start(backend);
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource();
        var pending = new HttpBleExplorer(client).ScanAsync(cancellation.Token);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        await backend.Released.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static BleClientOptions Options() => new() { DeviceAddress = "010203040506", ServiceId = Guid.Parse("3f63b38e-98df-4a52-a9f8-76b803a934b2"), CharacteristicId = Guid.Parse("f50d7529-4e5d-4088-b88f-e236cd1e7f29") };
    private static async Task<WebApplication> Start(Explorer? explorer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Services.AddLocalFlowRunner(new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Bluetooth.BluetoothPlugin()));
        if (explorer is not null) builder.Services.AddSingleton<IBleExplorer>(explorer);
        var app = builder.Build(); app.MapLocalFlowRunner(); await app.StartAsync(); return app;
    }
    private sealed class Explorer : IBleExplorer
    {
        public int Reads, Scans;
        public bool Wait;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation)
        {
            Scans++; Started.TrySetResult();
            try { if (Wait) await Task.Delay(Timeout.Infinite, cancellation); return [new("010203040506", BleAddressType.Random, "Test peripheral", -40)]; }
            finally { Released.TrySetResult(); }
        }
        public Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions options, CancellationToken cancellation) => Task.FromResult<BleServiceInfo[]>([new(options.ServiceId, [new(options.CharacteristicId, true, "Read")])]);
        public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) { Reads++; return Task.FromResult<byte[]>([0, 255, 42]); }
    }
}
