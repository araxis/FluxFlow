# BLE workflow components

Install **FluxFlow.Plugins.Bluetooth** from Studio’s Plugins page and restart the
companion. Its three nodes appear under **Devices**: BLE Client, BLE Notifications
and BLE GATT Server. The package also supplies the BLE peripheral and adapter
connection types. Removing it leaves existing nodes, settings and wires in saved
projects; reinstall it to edit and run them again. Shared Visual/JSON editors configure operations and a multi-service GATT
hierarchy. Bluetooth operations execute in the Windows companion; the browser
edits ordinary workflow configuration without accessing its Bluetooth APIs.

Windows hosts opt in when building:

```powershell
dotnet run --project Code/src/Studio/FluxFlow.StudioWebHost -p:EnableWindowsBle=true -f net10.0-windows10.0.19041.0
dotnet run --project Code/src/Hosting/FluxFlow.Runner -p:EnableWindowsBle=true -f net8.0-windows10.0.19041.0
```

The same option supports both .NET 8 and 10 hosts. With the package installed, portable hosts can
describe the components but reject native BLE execution with a platform error.
Check environment validates configuration and checks adapter role support and
radio state without opening a peripheral. Run checks again; neither action changes
the radio setting.
Docker is not required.

Client Read/Write operations can use configured targets or dynamic input commands.
Discovery is explicit and on demand. A separate bounded notification source supports
Notify and Indicate, unsubscribes on Stop and rejects late callbacks. Received values
preserve original bytes as `ValueBase64` plus `ByteLength`. Writes are not retried
automatically: cancellation cannot undo transmitted bytes.

One active server per companion supports up to eight distinct primary services,
each with up to 32 characteristics. Read/Write properties, access protection,
Notify/Indicate and supported descriptors are configurable. Workflow input updates
run-local values; remote writes emit original bytes to Output. Legacy single-service
configuration continues to load. Advertising remains separate and exposes only
the supported connectable/discoverable settings.

The curated UUID catalog and Battery/Tx Power templates are starting points, not
Bluetooth qualification or implementations of every standard service behavior.
Explicit Hex, Base64 and strict UTF-8 representations preserve bytes; selected
standard codecs never infer custom schemas. Named adapter/peripheral connections
reuse Infrastructure Catalog snapshots and compatible Environment bindings.

The tested Windows driver does not confirm advertising Stop reliably. Such cleanup
remains a failure and blocks another server start until the companion process is
restarted. Automated tests do not establish physical BLE delivery or over-the-air
shutdown; those require an independent BLE peer. See [the capability audit](CAPABILITIES.md)
for supported, Windows-managed and unsupported features and standards references.
