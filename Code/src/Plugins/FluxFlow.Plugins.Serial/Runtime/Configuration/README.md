# Serial text configuration

Register setup with `services.AddSerial()` from
`FluxFlow.Configuration.Components.Serial`, then use the existing
`AddFluxFlowApplication(configuration)` and host integration.

```json
{
  "Workflows": {
    "Device": {
      "Resources": {
        "Port": {
          "Type": "serial.port",
          "PortName": "COM3",
          "BaudRate": 115200,
          "DataBits": 8,
          "Parity": "None",
          "StopBits": "One",
          "Handshake": "None",
          "DtrEnable": false,
          "RtsEnable": false,
          "WriteTimeout": "00:00:02"
        }
      },
      "Serial": {
        "Type": "serial.text",
        "Connection": "Port",
        "NewLine": "\n",
        "MaxLineBytes": 65536,
        "InputCapacity": 128,
        "OutputCapacity": 128
      }
    }
  }
}
```

Use the actual operating-system port name, for example COM3 on Windows or
/dev/ttyACM0 on Linux. Access permissions and physical port checks are host concerns.
Registration and Build do not open a port; execution Start does.

All displayed connection/framing values except PortName are defaults. Connection is
required. It must resolve to a resource in the component's own workflow. Application
resources, cross-workflow references and a second component using the same resource
are rejected during registration. Native DI provides one port per workflow execution
scope and disposes it after the component. Restart creates a fresh port and component.

The standard Input, Output, Events and Error link properties are supported. Add
Output links to process received text; fan out through these links rather than adding
multiple readers. Error filters existing Output, without creating another physical port.
An upstream Source or Mapper can construct `{ "Text": "LED ON" }` and send it to
`Device.Serial.Input`. Property names are preserved; Text is case-sensitive.

Received data is a string, not automatically parsed JSON. Empty lines and whitespace
are preserved, excluding the LF/CRLF delimiter. Writes append the configured LF or
CRLF. MaxLineBytes applies to UTF-8 content, excluding the delimiter. Malformed UTF-8
and oversized incoming frames produce errors when their terminating LF arrives and
then recover. A partial final line is not published during stop.

Stop stops reception and input admission, drains accepted writes within their native
write timeouts, and closes the port. This does not guarantee device acknowledgement
or receive a reply to the final command. A port failure faults execution; there is no
automatic retry, reconnect or command replay. Workflow restart is available after a
successful stop, following the existing application lifecycle contract.

Hardware checks remain pending. Simulated I/O tests cover framing and lifecycle but
do not establish driver cancellation, unplug behavior or real-device delivery.
