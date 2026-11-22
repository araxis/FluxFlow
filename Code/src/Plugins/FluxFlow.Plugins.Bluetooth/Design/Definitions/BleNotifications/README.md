# BLE Notifications

Configuration type: `ble.notifications`. Subscribes to one remote GATT characteristic through
the Windows companion. Choose Notifications or Indications to match the device's properties.

## Ports

- **Output** emits original bytes as `ValueBase64` and `ByteLength`, the same format as BLE Client reads.
- **Events** uses the existing runtime event channel. There is no Input port.

## Configuration

Edit the device, subscription mode, UUIDs and execution settings in the Property Grid.
Enter a known target directly or use **Select BLE target** to discover it. A named
peripheral connection resolves through the selected environment. Scan and discovery
do not automatically pair or select a nearby device. **Advanced JSON** edits a
detached draft. Save is one undoable edit; Cancel does not modify configuration.

The timeout applies to subscription startup, not the lifetime of an established subscription.
Each notification starts a new message lineage using the existing receiving instrumentation.
The Windows callback queue is bounded by OutputCapacity. Overflow fails explicitly; the runtime's
normal output delivery semantics still apply. Notifications are not durable or guaranteed delivery.
The companion permits one active subscription per device/address type/service/characteristic.
Share a source's Output between consumers instead of independently changing the same subscription.

## Stop and failures

Stop rejects new callbacks, disables the characteristic's client configuration descriptor and
releases service/device handles. Cleanup is awaited even after cancellation. Windows can take
additional time to finish native I/O. Unsubscribe failures remain failures; they are not reported
as confirmed cleanup. Disconnection fails the source; it does not silently reconnect or resubscribe.
If the device disconnects during setup, the subscription does not report ready or emit buffered
values. Once ready, accepted values can drain before an overflow or disconnect is reported.
Stop can discard pending values, but does not erase a failure already observed by the subscription.

Requires an enabled BLE central-capable adapter on the Windows companion. The target characteristic
must support Notify or Indicate, respectively. Device authentication and permissions still apply.
An independent test peripheral is required to verify physical delivery and over-the-air cleanup.
