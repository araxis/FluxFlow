"""MicroPython peer for the compiled BLE Client and Notifications trials."""
import bluetooth
import time

service = bluetooth.UUID("b740c050-c85e-4415-ad58-8a944afb51ad")
value = bluetooth.UUID("22fc1cbf-04ed-4ca7-9c52-23f50ac1f741")
radio = bluetooth.BLE()
radio.active(True)
radio.config(addr_mode=0)
flags = bluetooth.FLAG_READ | bluetooth.FLAG_WRITE | bluetooth.FLAG_NOTIFY | bluetooth.FLAG_INDICATE
((handle,),) = radio.gatts_register_services(((service, ((value, flags),)),))
radio.gatts_write(handle, b"")
advertisement = b"\x02\x01\x06\x11\x07" + bytes(service)


def on_event(event, data):
    if event == 2:  # _IRQ_CENTRAL_DISCONNECT
        radio.gap_advertise(100_000, adv_data=advertisement)
    elif event == 3 and data[1] == handle:  # _IRQ_GATTS_WRITE
        radio.gatts_write(handle, radio.gatts_read(handle), True)


radio.irq(on_event)
radio.gap_advertise(100_000, adv_data=advertisement)
print("Public peer address:", bytes(radio.config("mac")[1]).hex())
while True:
    time.sleep(1)
