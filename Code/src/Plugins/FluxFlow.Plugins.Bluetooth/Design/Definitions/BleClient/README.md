# BLE Client

Reads or writes a GATT characteristic for each message arriving at **Input**. Configuration type: `ble.client`.
Runs through the Windows BLE companion, not through browser Bluetooth APIs.

## Configure

Use **Select BLE target** to choose a known peripheral's current **Device address**, **Address type**,
**Service UUID** and **Characteristic UUID**. The characteristic must support the selected operation.
Private/random addresses may change; this initial slice does not resolve bonded device identities.

Alternatively, select an optional **Peripheral connection** referencing a named
`ble.peripheral` connection. Its Endpoint is `ble://AABBCCDDEEFF/public` or `/random`.
Environment topology can bind it to a reusable peripheral snapshot. The connection
supplies the address during Run; direct address fields remain saved but are ignored.
Target discovery resolves the selected environment's bindings. Read preview requires a direct device.

UUID search and binary values use the shared GATT editors. The standard list is
curated, with explicit encoders/decoders for selected characteristics. Custom values
remain raw bytes; the editor never infers a custom payload format.

Enter a known target directly in the **Device target** Property Grid rows, or use the
group's selector to discover it. Fixed-operation fields are hidden when **Command from message** is selected;
their saved values remain intact. Write settings appear only for a fixed Write operation.
A named peripheral connection hides the manual address without deleting it.

In the selector, choose **Device source**: Direct device exposes the address; Named
connection lists compatible BLE peripheral connections. Missing saved references remain visible.
The **Device target** section groups the source, device address, address type and both UUIDs.
Its header's **Select BLE target** button opens a shared device/service/characteristic
selector. Opening it does not scan or connect. **Scan devices** in the toolbar populates
the device roots of a single **Device → Service → Characteristic** tree. Expanding or
selecting a device discovers only that device; its services appear underneath it.
**Enter device address manually** exposes address and address-type fields for a known device.
Selecting a device or named connection automatically discovers its GATT services; a manually entered
address is discovered when the field loses focus. Changing address type also refreshes discovery.
Expand a service in the tree and select a characteristic. Known UUIDs use the existing standard catalog labels;
hover over a label to see its full UUID. **Refresh services** retries discovery, and **Cancel discovery** stops a pending request.
Changing the target clears the previous device's GATT results and rejects late responses;
other scanned devices remain available. Discovery reads GATT metadata, not characteristic values.
**OK** applies the target and both UUIDs as one undoable edit. **Cancel** leaves everything unchanged.
In Command mode, the selector applies only the device or connection and preserves hidden UUIDs.
**Read preview** is in the Inspector's **Tools** tab and shows the original bytes without sending workflow messages.
Property error icons open the matching record in **Validation**. Configuration errors and failed
device-tool or conversion attempts are labeled separately; rejected conversions preserve the saved bytes.
Named connections use the active environment snapshot. Discovery rejects ambiguous bindings across
application instances, missing targets and remote-host adapters instead of using the saved direct address.

Choose **Input mode**: Fixed operation or Command from message. Command mode shows an
expandable example and retains hidden fixed settings.

Use **Advanced JSON** (or double-click the component) for optional full JSON editing.
Save validates the configuration
and creates one undoable edit; Cancel leaves the component unchanged. Unknown JSON
properties are preserved, and invalid JSON stays in the editor for correction.

**Scan devices** listens for advertisements for five seconds through the companion.
**Cancel**, Escape or closing the selector cancels pending work without changing the configuration.
Select your device to automatically load its GATT services and characteristics with their supported
properties. Use **Refresh services** to repeat discovery. The runtime validates the selected operation.
Advanced JSON remains available for configuration that cannot be discovered from a device.

**Read preview** displays the original bytes as Hex and Base64. A preview does not send
workflow messages or save settings. Changing the target clears the preview.
Canceling an operation or closing the editor rejects late results; the companion
still finishes native cleanup. Discovery does not enable the radio or pair devices.

## Ports

Connect Scheduler.Output to BLE Client.Input to read periodically. Connect Output to a
Logger or mapper. Output preserves the input message's metadata and contains:

```json
{"ValueBase64":"AQI=","ByteLength":2}
```

Base64 preserves the device bytes. It does not assume that a sensor value is text or a number.
The component does not select nearby devices or pair automatically. Use **BLE Notifications**
for a continuous notification/indication stream.

## Read and Write operations

Existing configurations continue to use fixed reads. Choose Write to configure a write per input.
Writes select WithResponse or WithoutResponse and a Value with Encoding (Hex, Base64 or Utf8)
and Data. Hex uses pairs of hexadecimal digits without separators. Values are limited to 512
bytes; the device and negotiated transport may impose a lower limit. No automatic fragmentation
or retry is applied. Write output contains ByteLength, WriteMode and Acknowledged. A protocol
acknowledgement does not confirm completion of a device-specific action.

Select **Command from message** to supply the operation and UUIDs dynamically. A write uses
the command's write mode (WithResponse if omitted) and payload, regardless of the saved fixed
operation. The device still comes from the configured address or peripheral connection.
Each input must include Operation, ServiceId and
CharacteristicId; Write also requires Value:

```json
{
  "Operation": "Write",
  "ServiceId": "3f63b38e-98df-4a52-a9f8-76b803a934b2",
  "CharacteristicId": "f50d7529-4e5d-4088-b88f-e236cd1e7f29",
  "WriteMode": "WithResponse",
  "Value": { "Encoding": "Hex", "Data": "01" }
}
```

Read operations omit WriteMode and Value. Property and enum names are case-insensitive.
WithoutResponse writes do not provide a protocol acknowledgement. Cancellation cannot undo
bytes already transmitted; a write timeout does not prove the device command was not executed.

## Execution

The execution host needs BLE central support and its radio enabled. Operations are serial and
bounded by the component queues. Timeout requests cancellation; Windows can take longer to
finish native operations. A disconnect reported by the provider fails that input; the next
input makes a new attempt. The client does not replay the failed request. A timeout or
abort rejects late success; the client still waits for native cleanup. During graceful
Stop, an already accepted operation can finish and its output remains in that run.
Use the native Events/error handling and Logs/Trace panels for failures.

No physical client delivery has been verified yet because no independent test peripheral
was available. Configuration and Undo/Redo do not access the Bluetooth radio.
