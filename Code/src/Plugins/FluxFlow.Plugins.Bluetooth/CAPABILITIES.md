# BLE capability audit

Reviewed 2026-11-02. This is the implemented Windows companion subset, not a claim
of Bluetooth qualification or complete GATT profile support. Hardware delivery and
over-the-air shutdown still require an independent test peripheral/central.

## Supported by this implementation

| Area | Scope |
| --- | --- |
| GATT server | One active server per companion, with 1–8 distinct primary service UUIDs; 1–32 characteristics per service |
| Properties | Read, Write, WriteWithoutResponse, Notify, Indicate |
| Protection | Plain, AuthenticationRequired, EncryptionRequired, EncryptionAndAuthenticationRequired; Windows enforces access and pairing policy |
| Descriptors | User Description through the Windows property; up to 16 custom UUID read-only descriptors per characteristic with explicit bytes/read protection |
| Values | Hex, Base64, strict UTF-8; at most 512 bytes; original received bytes retained as Base64 |
| Standard helpers | Battery Level uint8 0–100; Tx Power Level sint8 -100–20 dBm; Manufacturer/Model/Serial strings as strict UTF-8 |
| Templates | Basic Battery Service with Read, or Read and Notify; Tx Power with exactly one read-only Tx Power Level. Initial 100% and 0 dBm are editable examples, not measured values. Workflows must supply real values and request notifications when required. Studio does not measure or set radio power. |
| Client | Explicit device address/type, discovery, Read, Write with/without response, Notify/Indicate subscriptions |
| Infrastructure | Named peripheral and default-adapter connections, existing-device catalog entries, locked snapshots and compatible topology bindings |

The searchable UUID list is a curated subset of Assigned Numbers. Generating a
custom UUID uses a random 128-bit UUID outside the Bluetooth base UUID namespace.
Selecting a standard UUID does not implement the associated service behavior.
No custom payload schema is inferred. Switching a value's representation preserves
its bytes; invalid UTF-8 conversion is rejected. Decoding is display-only until an
explicit encoding edit is applied.

## System-managed and deliberately unsupported

Windows reserves GAP (1800), GATT (1801), Device Information (180A), and Scan
Parameters (1813). The editor lists them for reference/client use but rejects server
publication. Windows generates CCCD (2902) for subscriptions and User Description
(2901) from its property. Presentation/Aggregate Format (2904/2905) and Extended
Properties (2900) are not editable in this implementation. Standard descriptor UUIDs
cannot be entered as custom descriptors. See [Windows GATT server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server).

Included/secondary services, duplicate service UUID instances, reliable/queued
writes, writable descriptors, broadcast, signed writes, explicit MTU negotiation,
custom pairing agents, identity resolution for rotating addresses, remote radio
proxies and selection of nondefault adapters are not implemented. Offset writes
are rejected. Long reads respect the requested offset and length.

Advertising is separate from the GATT hierarchy. This host exposes only the
connectable/discoverable flags. Windows selects the radio address, local name and
advertising packet; custom manufacturer data, appearance, service-data sections,
advertising interval and extended-advertising controls are not exposed here.
Available advertising space can prevent every service UUID from appearing in the
packet. Native status remains visible; it is not rewritten as full advertising
success. Service discovery after connection is distinct from the advertising packet.

## Host ownership and checks

`ble.peripheral` uses `Endpoint: "ble://AABBCCDDEEFF/public"` (or `/random`). A client
or notification component can refer to it with `Connection`. It identifies a nearby
peripheral; it is not an IP endpoint and does not tunnel through the catalog host.
`ble.adapter` uses `Endpoint: "ble-adapter://default"`; the server's optional `Adapter`
reference must belong to its execution host. Catalog entries on other hosts remain
descriptive; the local companion cannot use their adapters. Project-owned adapter
blocks can be placed on the local host. Network boundaries do not route BLE traffic.

Check environment reads native default-adapter availability, required central or
peripheral role and radio state. It does not enable Bluetooth, scan, connect, pair
or verify a peripheral's reachability. Run checks native access again. Old projects
with direct device addresses and legacy ServiceId/Characteristics continue to load.

## Lifetime and verification

Each run owns a detached value snapshot, bounded queues and native callbacks.
Incoming writes emit original bytes to Output; input updates affect only this run.
Notify sends to a snapshot of subscribed clients after checking their maximum sizes;
oversized values are rejected rather than silently truncated. An update with no
subscribers still updates the local value and is not evidence of remote delivery.
Partial transmission or cancellation cannot undo bytes already sent.

Startup acquires all services and rolls them back in reverse order if configuration,
advertising or cancellation fails. Stop closes admission, detaches handlers, drains
outstanding requests/deferrals and stops each advertised provider. Late callbacks
cannot update a closed session. Client subscription cleanup removes handlers and
attempts CCCD unsubscribe before disposing native ownership. Native failures are
retained through existing logging and OpenTelemetry paths.

The previously observed **unconfirmed GATT Stop** remains an explicit limitation:
if Windows does not confirm Stopped within the existing deadline, cleanup fails and
another server run on that companion is refused. Restarting the companion is needed
before retry; it does not prove over-the-air shutdown. No hardware cleanup success
is claimed by deterministic tests.

The Windows proof exercises rollback, pending requests, late events and unconfirmed
Stop without radio access on .NET 8 and 10. Component/companion/editor tests cover
binding snapshots, values, Stop retention, reruns, invalid JSON and Undo/Redo.

## Standards references

- [Bluetooth Assigned Numbers](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/Assigned_Numbers/out/en/index-en.html): identifiers, not implementation capabilities.
- [Battery Service 1.1](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/BAS_v1.1/out/en/index-en.html): basic Battery Level shape. Optional BAS features and multiple-instance presentation descriptors are outside this subset.
- [Tx Power Service 1.0](https://www.bluetooth.com/specifications/specs/tx-power-service-1-0/): a single primary service and one read-only current-power characteristic. The template supplies the structure; the project must supply the actual value.
- [Device Information Service 1.2](https://www.bluetooth.com/wp-content/uploads/Files/Specification/HTML/DIS_v1.2/out/en/index-en.html): string formats used by client-side helpers; Windows reserves server publication.
- [GATT Specification Supplement, 2026-09-09](https://btprodspecificationrefs.blob.core.windows.net/gatt-specification-supplement/GATT_Specification_Supplement.pdf), section 3.262: Tx Power Level encoding.
- [Windows notification API](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattlocalcharacteristic.notifyvalueasync): per-client notification results.
