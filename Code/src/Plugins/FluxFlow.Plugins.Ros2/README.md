# ROS 2 configuration

This plugin supplies the ROS node connection and runnable `ros.publisher` and
`ros.subscriber` components for **std_msgs/msg/String**. Its generated binding is
included in the package. Installing or editing the plugin does not load native ROS.
The execution host still needs ROS and the std_msgs native type-support libraries.

Use the shared Property Grid to select **Node**, set **Topic**, and edit **QoS**,
subscriber queue policy and output capacity. Advanced JSON remains optional.
Input and received output values use `{ "Data": "hello" }`; raw strings are not
the message contract. UTF-8 text, including Unicode and newlines, is preserved.
An empty string is valid; an omitted Data field keeps the generated empty-string
default. An explicit null Data field is rejected before native publication.

Custom hosts can continue registering other compiled message types with the existing APIs:

```csharp
using FluxFlow.Configuration.Components.Ros2;

services.AddRosNode();
services.AddRosPublisher<Rosidl.Messages.Std.String>("ros.string.publisher");
services.AddRosSubscriber<Rosidl.Messages.Std.String>("ros.string.subscriber");
services.AddFluxFlowApplication(configuration);
```

Use a distinct type key for each generated message type. Existing custom registrations
for `ros.publisher` or `ros.subscriber` are retained when the plugin is configured;
custom hosts are responsible for documenting their different payload contracts.
There is no assembly scan,
type-name reflection or message catalog. The generic registration closes the component
factory with the host's compiled message contract.

```json
{
  "Resources": {
    "ros": { "Type": "ros.node", "Name": "workflow", "Namespace": "/" }
  },
  "Workflows": {
    "main": {
      "receive": {
        "Type": "ros.subscriber", "Node": "ros", "Topic": "/incoming",
        "QueueSize": 64, "FullMode": "DropOldest", "Output": "send.Input"
      },
      "send": {
        "Type": "ros.publisher", "Node": "ros", "Topic": "/outgoing"
      }
    }
  }
}
```

Node references use the existing workflow/application resource lookup. Share an
application-level resource between workflows; workflow-local resources belong to
their own workflow. Missing saved references are retained for repair. Each resource
uses the one host IRclContext registered by AddRosNode; an existing host context
registration is respected. Optional host arguments are copied during AddRosNode.
ROS_DOMAIN_ID, middleware selection and native-library paths remain host environment
settings. Factories do not load native ROS until Build resolves the node resource.
Endpoints and nodes are released before the context through ordinary DI ownership.

Publisher and Subscriber accept Qos with native QosProfile property names and enum
values, for example:
`"Qos": { "History": "KeepLast", "Depth": 5, "Reliability": "BestEffort", "Durability": "Volatile" }`.
Omitting Qos uses Rcl.NET's default profile. When specifying Qos, supply the desired
profile explicitly: unspecified fields use the native QosProfile constructor defaults.

Subscriber also exposes IgnoreLocalPublications, QueueSize and FullMode. Wait is
unsupported by Rcl.NET's delivery queue; use a native drop mode. OutputCapacity
defaults to 128. Standard Output/Events/Error links use the existing typed references;
Publisher additionally supports Input. Error is a selection on Output, not another port.

Input JSON contains ROS message fields, not a FluxFlow envelope:
`{ "Data": "hello" }` for std_msgs/msg/String. Headers and trace IDs remain internal;
a subscriber creates a new envelope unless a future explicit wire contract carries them.

Stop cancels the subscription read and disposes its native endpoint. Publication
has no native cancellation token: Stop waits for an in-flight publish before
disposing the publisher. A blocked middleware call can therefore prevent timely
shutdown. Output confirms local publication, not subscriber receipt or processing.
DDS discovery, compatible QoS and any network/firewall configuration remain native
requirements. Queue drop modes are lossy; this component does not add durable delivery.

The installed-package String delivery and Stop/rerun trial is in
`eng/ros2-plugin-consumer`. The custom typed-host example remains in `eng/ros2-consumer`.
