# Standard String binding

`String.g.cs` is the ros2cs 2.1.2 binding for `std_msgs/msg/String` from
ROS Jazzy `std_msgs` 5.3.8 (ros2/common_interfaces tag 5.3.8).
The interface is distributed under Apache License 2.0; see LICENSE.
The generated code is kept here so building and installing this plugin does
not require a ROS installation or network access for message generation.

To reproduce, source the Jazzy environment and run ros2cs 2.1.2 with
`--from-ament-index --include std_msgs --namespace FluxFlow.Plugins.Ros2.Messages`.
Retain only `Std/Messages/String.g.cs`; this message has no message dependencies.
Native std_msgs type support remains an execution-host prerequisite.
