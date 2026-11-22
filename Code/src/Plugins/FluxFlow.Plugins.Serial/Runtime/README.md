# FluxFlow.Components.Serial

`serial.writer` accepts exactly one payload per message: `{"Bytes":[255,0,127]}`,
`{"DataBase64":"/wB/"}`, or `{"Text":"example"}`. Bytes are JSON integers from 0 to
255. All three inputs use the configured framing and size limit; Raw writes add
nothing to the payload.

`serial.reader` defaults to its original `DataBase64`, `Text`, and `DecodingError`
fields. Set **Output format** to **Bytes** to receive `Bytes` instead of
`DataBase64`; text and decoding errors are unchanged. Raw reads are chunks:
a device packet can span reads, and one read can contain several packets.

`SerialText` is one duplex, newline-delimited UTF-8 session over a native
`System.IO.Ports.SerialPort`. The resource is workflow-local and has one component
owner. DI disposes the port; the component opens it at Start and, on normal stop,
closes it after draining accepted writes. Nothing opens during configuration
registration or Build.

Input accepts `{ "Text": "LED ON" }`. Writes execute sequentially and append LF
(or configured CRLF). Embedded CR/LF and text exceeding MaxLineBytes are rejected.
Output publishes received lines as string-valued FlowMessages. LF and CRLF are
accepted; other whitespace and empty lines are preserved. There is no automatic
JSON parsing, echo, request/reply correlation, reconnect or retry. A successful
local write does not acknowledge execution by the device.

Partial lines and split UTF-8 characters survive read timeouts. Invalid UTF-8 or
an oversized frame produces an error on Output at the next LF, then reception
continues. Buffering is bounded by MaxLineBytes (65536 by default). Output uses
the standard broadcast behavior; it is not a durable queue.

Events include serial.connected, serial.written (with the write's message lineage),
serial.disconnected and serial.connection_failed. Invalid commands become ordinary
processing errors. Port open/read/write failures fault execution. A write timeout
may mean a partial write, so further commands are not sent and writes are not retried.
Queued writes are dropped after a fault, without replay. A read failure cannot
interrupt a native write already in progress; that write may still finish.

Normal stop closes input admission, stops reception, drains accepted writes, then closes
the port. An unfinished received line is discarded; stop does not wait for device
replies. Idle reads use a finite 250 ms timeout; writes use the resource timeout.
Unexpected failures still require disposal to release the port. Workflow restart
after a successful stop creates fresh component and resource instances using the
existing workflow scope. A faulted execution cannot be restarted with StartAsync.

With AddFluxFlow and the default Generic Host behavior, a runtime serial failure
requests host shutdown. Fatal I/O failures are reported through execution completion
and host logging; an Output/Error link is not a recovery mechanism. Stop and disposal
can report the original failure even after releasing the port. After correcting the
cause, run a new host/application instance; commands from the failed run are not replayed.

For direct use, supply an unopened SerialPort with finite positive ReadTimeout and
WriteTimeout. The caller retains disposal ownership. Two sessions must not share
one physical port. See the [Serial configuration guide](Configuration/README.md) for registration and framing settings.

Hardware/virtual-port validation remains pending: native open/close, driver timeout
behavior, unplug and reconnect after workflow restart. Automated tests exercise
the component through internal native-I/O delegates; they do not validate a driver.
