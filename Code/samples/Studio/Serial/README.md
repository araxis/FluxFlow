# Serial loopback

Open `loopback.fluxflow` in Studio. In Connections, edit the workflow's `port`
and choose a known loopback device on the companion computer. Replace the placeholder
PortName and set the correct line settings before Run. Do not aim this command
sample at an unidentified device.

The scheduler writes bytes `01 02 03` followed by LF every second. Serial Reader
uses the same port session, waits for LF, preserves `AQID` as DataBase64 and logs it.
A physical loopback should connect transmit to receive using the appropriate
electrical interface. A browser-only simulation does not verify a serial driver.

Check environment validates configuration and ownership without opening hardware.
The port list only enumerates the companion's port names; it does not test access.
Stop drains admitted writes, stops reads and disposes the run-owned session.
Unplugging, access denial and occupied ports fail explicitly. A partial write is
not retried. Physical hardware verification has not been performed for this sample.

Infrastructure catalog entries use `serial-port://local/COM3` or an encoded POSIX
path such as `serial-port://local/%2Fdev%2FttyUSB0`. These identify physical devices,
not network proxies. In the project, mark the catalog host as **This computer**
and bind the application's serial connection to its locked device snapshot.
The binding changes PortName only; baud rate and framing remain project settings.
