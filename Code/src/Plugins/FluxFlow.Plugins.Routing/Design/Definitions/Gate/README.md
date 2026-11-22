# Gate

Controls when messages may pass through the workflow. Configuration type: `gate`.

## Setup

**Initially open** sets the starting state; it defaults to false.
Use separate workflow links to drive the **Open** and **Close** inputs.

## Ports

- **Input** receives the messages being controlled.
- **Open** and **Close** change the gate state; their message values are ignored.
- **Output** forwards input unchanged while the gate is open.
- **Events** is the component event port; **Error** is filtered Output.

Messages processed while closed are discarded, including errors; they are not held
for later release. Open/Close and Input are separate queues, so admission of an Open
command alone does not guarantee it has been processed before a message on Input.

Gate controls flow within a running workflow. It does not start or stop the workflow
or its external services. `OutputCapacity` defaults to 128.
