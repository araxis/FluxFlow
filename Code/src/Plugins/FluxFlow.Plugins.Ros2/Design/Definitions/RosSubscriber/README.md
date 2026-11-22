# ROS 2 Subscriber

Receives **std_msgs/msg/String** from a topic as `{ "Data": "hello" }`.
Configuration type: `ros.subscriber`. The standard binding ships with the plugin.

## Setup

Define a **ROS node** in **Connections → Add connection**, then select it in
**Node** and enter the **Topic**. The **QoS** group edits subscription
delivery policies. The execution host needs native ROS and std_msgs type support;
Check validates configuration without proving native availability or DDS discovery.

**Queue size** defaults to 64. **Full queue mode** controls overflow (default Drop oldest).
**Ignore local** defaults to false. Select a queue policy suitable for
whether losing older sensor readings is acceptable.

## Ports and ownership

There is no Input. **Output** emits received values; **Events** is the component event
port and **Error** filters Output errors. `OutputCapacity` defaults to 128.
Shutdown releases the subscription. The component borrows the node resource;
disposing the shared node belongs to its owner.
Each received value gets a new envelope: publisher trace and correlation metadata
are not carried by the standard ROS String wire contract. Native subscription
failure faults the source. A new run creates fresh subscription endpoints.
