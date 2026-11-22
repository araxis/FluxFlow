using FluxFlow.Application.Addressing;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Application;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Bluetooth;

internal sealed class BleHostSupport(IBlePlatform platform) : IWorkflowHostSupport
{
    public void ConfigureInstance(IServiceCollection instance) => instance.AddSingleton(platform);

    public async Task CheckAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var checkedRoles = new Dictionary<bool, string?>();
        var errors = new List<string>();
        foreach (var workflow in configuration.GetSection("Workflows").GetChildren())
        foreach (var component in workflow.GetChildren())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = component["Type"];
            if (type is not ("ble.client" or "ble.notifications" or "ble.server")) continue;
            var peripheral = type == "ble.server";
            try
            {
                if (platform is UnavailableBlePlatform)
                    throw new InvalidOperationException("BLE requires a Windows BLE-enabled companion. This host cannot access native Bluetooth.");
                var reference = component[peripheral ? "Adapter" : "Connection"];
                if (reference is not null)
                {
                    var expected = peripheral ? "ble.adapter" : "ble.peripheral";
                    var resource = ApplicationAddress.ResolveResourcePaths(reference, workflow.Key)
                        .Select(path => configuration.GetSection((path.WorkflowName is null ? "" : "Workflows:") + string.Join(":", path.Segments)))
                        .FirstOrDefault(section => section.Exists());
                    if (resource?["Type"] != expected) throw new ArgumentException($"'{reference}' must name a {expected} connection.");
                    var connection = new BleConnection(resource["Endpoint"] ?? "");
                    if (peripheral) connection.ValidateAdapter(); else connection.Apply(new());
                }
                if (platform is not IBleCapabilities capabilities) continue;
                if (!checkedRoles.TryGetValue(peripheral, out var error))
                {
                    try { await capabilities.CheckAsync(peripheral, cancellationToken).ConfigureAwait(false); }
                    catch (Exception failure) when (failure is not OperationCanceledException) { error = failure.Message; }
                    checkedRoles.Add(peripheral, error);
                }
                if (error is not null) errors.Add($"Component '{component.GetComponentAddress()}': {error}");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                errors.Add($"Component '{component.GetComponentAddress()}' ({type}): {error.Message}");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
