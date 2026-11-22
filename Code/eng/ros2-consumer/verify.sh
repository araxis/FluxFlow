#!/usr/bin/env bash
set -eo pipefail
cd "$1"
source /opt/ros/jazzy/setup.bash
set -u
export ROS_DOMAIN_ID=173
export ROS_AUTOMATIC_DISCOVERY_RANGE=LOCALHOST
export NUGET_PACKAGES="$(realpath -m ../packages)"
for framework in net8.0 net10.0; do
    output="$(realpath -m "../publish-$framework")"
    dotnet publish Ros2Consumer.csproj -c Release -f "$framework" -r linux-x64 --self-contained true "-p:FluxFlowVersion=$2" -o "$output"
    python3 verify.py --host "$output/Ros2Consumer"
    python3 verify.py --host "$output/Ros2Consumer" --project topic-relay.fluxflow
done
