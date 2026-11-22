# ROS 2 Publisher

Publishes **std_msgs/msg/String** to a topic. Configuration type: `ros.publisher`.
Send an object such as `{ "Data": "hello" }` to Input. The standard binding ships
with the plugin; no message generation is required for this type.
Empty strings are valid. Omitting Data keeps its empty-string default, while an
explicit null is a conversion error and is not published.

## Setup

Select **Connections → Add connection → ROS node** to define a node name and namespace.
Choose that resource in **Node** and set **Topic**. Set delivery policies in
the **QoS** group, or leave the profile unset to use publisher defaults.

## Ports

**Input** receives message objects. Successful **Output** preserves the original
input and its correlation, confirming local publication rather than remote receipt;
**Error** filters Output errors. **Events** is the component event port.
`OutputCapacity` defaults to 128.

The component borrows the node resource; its owner controls that resource's disposal.
The execution host must provide native ROS and std_msgs type support. Check validates
configuration without loading those libraries. Stop waits for an in-flight native
publish because Rcl.NET does not offer cancellation for that operation. Custom typed
host registrations remain supported; their host defines the payload contract.
