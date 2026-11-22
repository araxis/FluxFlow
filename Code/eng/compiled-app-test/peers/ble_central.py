"""MicroPython peer for the compiled BLE GATT Server trial. Requires aioble."""
import asyncio
import bluetooth

import aioble


service_id = bluetooth.UUID("3bb4a974-fe8d-4c04-b192-84407af83f31")
value_id = bluetooth.UUID("ff9258a3-dd7c-421b-a6f1-b52bf8b210f6")


async def find_server():
    async with aioble.scan(3000, interval_us=30000, window_us=30000, active=True) as scan:
        async for result in scan:
            if service_id in result.services():
                return result.device


async def exercise(device):
    async with await device.connect(timeout_ms=7000) as connection:
        service = await connection.service(service_id)
        if not service:
            return
        value = await service.characteristic(value_id)
        if not value or await value.read() != b"\x00\xff":
            return
        await value.write(b"\x80\x01", response=True)
        if await value.read() != b"\x80\x01":
            raise ValueError("GATT Server read-back differs from the accepted write")
        # This second write proves that the remote read-back succeeded.
        await value.write(b"\x55\xaa", response=True)
        print("Verified initial value, write and read-back.")


async def main():
    while True:
        try:
            device = await find_server()
            if device:
                await exercise(device)
        except (OSError, asyncio.TimeoutError, aioble.DeviceDisconnectedError, aioble.GattError) as error:
            print("Connection ended:", error)
        await asyncio.sleep_ms(500)


asyncio.run(main())
