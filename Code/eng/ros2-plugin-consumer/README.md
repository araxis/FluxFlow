# Installed ROS String trial

This host has no ROS plugin or Rcl.NET project reference. It loads the installed
package through the same plugin loader as the companion. An independent rclpy
peer verifies empty strings, Unicode/newlines, non-BMP characters, native endpoint disappearance on
Stop, and a fresh run in the same process. Messages published while stopped must
not appear in the next run.

Use an isolated plugin store, package output and publish output. Pack
`src/Plugins/FluxFlow.Plugins.Ros2/FluxFlow.Plugins.Ros2.csproj` and install its
`.nupkg` with `eng/fluxflow-plugin.ps1 -Action Install -Source <package> -Directory <store>`.
Publish this project for each of `net8.0`, `net10.0` and `net11.0`, with
`-r linux-x64 --self-contained true` for the Jazzy Linux trial.

In a ROS Jazzy shell with rclpy and std_msgs installed:

```sh
source /opt/ros/jazzy/setup.bash
export ROS_DOMAIN_ID=173
export ROS_AUTOMATIC_DISCOVERY_RANGE=LOCALHOST
python3 verify.py --host /absolute/path/to/Ros2PluginConsumer --store /absolute/path/to/store
```

The verifier uses unique topic names and removes its own native endpoints and
process on failure. It never connects to robot topics. A pass proves local DDS
delivery for the tested host/middleware; it does not certify remote discovery,
other ROS distributions, Windows native libraries or bounded shutdown of a
blocked native publish.
