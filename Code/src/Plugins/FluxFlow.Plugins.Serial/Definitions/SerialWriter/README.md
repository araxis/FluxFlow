# Serial Writer

Writes bytes or text through a workflow-owned serial session. Configuration type:
`serial.writer`. Shares a workflow-local `serial.port` resource with Serial Reader
and other Writers without opening it twice.

## Configuration

`Connection` references the resource containing physical port and line settings.
Framing, DelimiterBase64, FrameLength, MaxFrameBytes, Encoding and Capacity use the
shared serial frame editor, including synchronized Visual/JSON modes.

`Raw` writes exactly the supplied bytes. `Delimiter` appends the configured delimiter
and rejects a payload containing that delimiter. `FixedLength` requires exactly
FrameLength bytes. Text encoding is explicit; None requires DataBase64.

## Ports

- **Input**: `{ "DataBase64": "AP8B" }` or `{ "Text": "status" }`.
  Supply exactly one representation. Base64 preserves arbitrary binary bytes.
- **Output**: the original successful command, with existing workflow correlation.
  It means the native write completed, not that the device processed the command.
- **Events**: standard workflow lifecycle events.

## Delivery and cleanup

Writes from all Writers sharing a session are serialized. Buffers and frame sizes
are bounded. Invalid framing or encoding produces an error without transmitting.
A timeout or I/O failure may mean a command was partially transmitted. It faults
the session and blocks further commands; no automatic retry is attempted.

Stop drains admitted workflow work using the existing execution lifecycle. Native
writes already started must finish or reach their finite WriteTimeout before port
cleanup can be confirmed. The session retains ownership if Close fails. New runs
receive new sessions, and late commands are rejected. Remote physical-port access
and arbitrary payload schema inference are not provided.
