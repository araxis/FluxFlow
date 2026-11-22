# Serial Text

Reads and writes newline-delimited UTF-8 text. Configuration type: `serial.text`.

## Setup

**Connection** references a workflow-local `serial.port` resource containing the port
name and baud rate. One Serial Text component owns each port for the workflow execution.
**New line** is the delimiter appended to writes, such as `"\n"` or `"\r\n"`.
Incoming lines accept LF and CRLF.
**Maximum line bytes** bounds incoming lines; **Input capacity** bounds queued writes.

## Ports and lifecycle

**Input** accepts `{"Text":"LED ON"}`. Text must not contain CR or LF.
**Output** emits received lines as string values, plus recoverable processing errors.
Successful writes emit `serial.written` on **Events**, not a reply on Output.
**Error** filters Output errors. There is no automatic device reply correlation or retry.
`OutputCapacity` defaults to 128.

The workflow opens the port at startup and releases it on shutdown. Restart uses a
fresh execution after a successful stop. Fatal open/read/write failures fault execution;
they cannot be recovered through an Error link or by restarting the faulted execution.
After cleanup and correction, start a new host/application instance. An HTTP acknowledgement upstream
does not guarantee a queued command reached the device. Hardware access belongs to
the runner, not the browser.
