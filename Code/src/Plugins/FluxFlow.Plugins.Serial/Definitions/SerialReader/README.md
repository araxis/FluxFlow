# Serial Reader

Reads a physical serial port through a workflow-owned session. Configuration type:
`serial.reader`. Use Serial Writer with the same connection for duplex communication.

## Configuration

`Connection` names a workflow-local `serial.port` resource. The resource sets
PortName, BaudRate, DataBits, Parity, StopBits, Handshake, DtrEnable, RtsEnable,
ReadTimeout and WriteTimeout. Timeouts are finite. The port is opened on the
companion's computer, not in the browser. Manual port names remain supported.

Choose `Framing`: `Raw` emits native read chunks with no message-boundary guarantee;
`Delimiter` retains partial data until DelimiterBase64 is received; `FixedLength`
retains data until FrameLength bytes arrive. Delimiters are excluded from payloads.
`Cg==` means LF, `DQo=` means CRLF. MaxFrameBytes bounds retained bytes; an oversized
frame faults rather than silently truncating data. Capacity bounds output messages.

Optional Encoding is None, Utf8, Ascii, Utf16LittleEndian or Utf16BigEndian.
Invalid text preserves the original DataBase64 and reports DecodingError.
The shared Visual/JSON editor preserves other configuration fields.

## Ports

- **Output**: `{ "DataBase64": "AQID", "Text": null, "DecodingError": null }`.
  DataBase64 contains the exact frame payload bytes. Text is only a decoded view.
- **Events**: standard workflow lifecycle events.

## Ownership and cleanup

One Reader owns the receive stream; fan out its Output through workflow links.
Any Writers sharing this resource serialize their writes. Serial Text is an
exclusive legacy session and cannot share its port with Reader/Writer components.
Different resources, workflows and application instances cannot own the same port.
The host also rejects concurrent sessions before opening; the OS remains responsible
for conflicts with other programs and device aliases not identifiable by port name.

Stop cancels receive work and discards incomplete frames. The workflow disposes the
shared session after components stop. Read cancellation waits at most the configured
native read timeout in a functioning driver. Unplugging or native read failure
faults the session; restarting requires a new run. No automatic device-command retry
or remote serial proxy is implied. Physical loopback verification requires hardware.
