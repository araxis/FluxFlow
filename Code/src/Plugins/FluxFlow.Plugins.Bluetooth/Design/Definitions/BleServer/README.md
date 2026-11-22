# BLE GATT Server

Publishes primary services through the Windows companion while the workflow runs.
Configuration type: `ble.server`. One active server can contain several services.

## Configure

Open **Edit GATT service** in the Inspector or double-click the block. The hierarchy
shows services and their characteristics; **Advertising** is a separate selection.
Add a service, generate a custom UUID, or start from a Battery Level or Tx Power template.
The UUID search lists a curated set of Bluetooth standard identifiers and their
Windows restrictions. Templates provide example values, not hardware measurements.
Tx Power contains one read-only characteristic initialized to an example 0 dBm;
replace it with the actual device transmit power. Studio does not measure or set it.

Characteristics expose Read, Write, WriteWithoutResponse, Notify and Indicate,
read/write protection, explicit Hex/Base64/UTF-8 values and User Description.
Custom descriptors are read-only. Windows generates the subscription descriptor;
reserved standard descriptors cannot be added manually. Values are limited to 512
bytes and notifications can have a smaller negotiated limit.

Visual and JSON edit the same detached draft. Save applies one undoable edit;
Cancel discards it. Invalid JSON stays open for correction. Unknown fields are
preserved. Legacy ServiceId/Characteristics projects continue to load; adding a
second service uses the Services array instead. Do not combine both forms.

```json
{
  "Type": "ble.server",
  "Services": [
    {
      "Id": "3f63b38e-98df-4a52-a9f8-76b803a934b2",
      "Name": "Control",
      "Characteristics": [
        {
          "Id": "f50d7529-4e5d-4088-b88f-e236cd1e7f29",
          "Properties": "Read, Write, Notify",
          "InitialValue": { "Encoding": "Hex", "Data": "00FF" },
          "ReadProtection": "Plain",
          "WriteProtection": "EncryptionRequired"
        }
      ]
    }
  ],
  "IsConnectable": true,
  "IsDiscoverable": true
}
```

An optional **Adapter connection** names a `ble.adapter` resource. Otherwise the
companion uses its default adapter. Environment topology can bind the named resource
to an adapter on the application's host. Remote adapters are unsupported.

## Ports

Send an object to **Input** to update a run-local characteristic:

```json
{
  "ServiceId": "3f63b38e-98df-4a52-a9f8-76b803a934b2",
  "CharacteristicId": "f50d7529-4e5d-4088-b88f-e236cd1e7f29",
  "Value": { "Encoding": "Hex", "Data": "0142" },
  "Notify": true
}
```

Notify requests delivery to current subscribers using their selected Notify/Indicate
mode. It does not establish a subscription. With no subscribers it only updates the
local value. **Output** includes ServiceId, CharacteristicId, Operation
(`Updated` or incoming `Write`), ValueBase64 and ByteLength. Incoming writes retain
their original bytes; reads return the current run-local value. Updating a value
does not change the saved configuration or Undo/Redo history.

**Events** reports advertising startup. Startup, errors and native diagnostics use
the existing logs and OpenTelemetry instrumentation. Stop drains outstanding native
requests and restores initial values on the next run.

## Advertising and Windows limits

Allow connections and Discoverable default to enabled. Windows manages the local
name, address and advertising payload. Characteristic values are not advertising
data. The companion does not expose custom manufacturer data or advertising intervals.
An enabled radio and peripheral-role support are required; Check environment is read-only.

Windows reserves GAP, GATT, Device Information and Scan Parameters. This subset
supports 1–8 distinct primary services with 1–32 characteristics each. Included
services, duplicate service UUID instances, reliable/offset writes and automatic
payload fragmentation are unsupported.

The existing adapter limitation remains: Windows may continue reporting Started
after StopAdvertising. An unconfirmed Stop is a failure, not successful cleanup;
further server runs are rejected until the companion restarts. An independent peer
is still required to verify delivery and over-the-air shutdown. Editing, saving and
Undo/Redo do not activate the radio.
