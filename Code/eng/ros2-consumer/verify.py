"""Exercise packaged topic components against independent Python ROS endpoints."""
from pathlib import Path
import argparse
import subprocess
import tempfile
import time
import uuid

import rclpy
from std_msgs.msg import String
from geometry_msgs.msg import Twist

parser = argparse.ArgumentParser()
parser.add_argument("--host", type=Path, default=Path(__file__).parent / "bin/Release/net10.0/Ros2Consumer.dll")
parser.add_argument("--project", type=Path)
options = parser.parse_args()
prefix = "/fluxflow_test_" + uuid.uuid4().hex
rclpy.init()
node = rclpy.create_node("fluxflow_test_peer")
strings = []
vectors = []
sub = node.create_subscription(String, prefix + "/output", lambda m: strings.append(m.data), 10)
vector_sub = node.create_subscription(Twist, prefix + "/twist_output", lambda m: vectors.append(m), 10)
pub = node.create_publisher(String, prefix + "/input", 10)
vector_pub = node.create_publisher(Twist, prefix + "/twist_input", 10)
command = ["dotnet", str(options.host)] if options.host.suffix == ".dll" else [str(options.host)]
command.append(prefix)
if options.project:
    command.append(str(options.project))
with tempfile.TemporaryFile(mode="w+") as log:
    process = subprocess.Popen(command, stdin=subprocess.PIPE,
                               stdout=log, stderr=subprocess.STDOUT, text=True)
    try:
        deadline = time.monotonic() + 35
        marker = "ros-unicode-\u03bb-" + uuid.uuid4().hex
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f"Consumer exited early: {process.returncode}")
            pub.publish(String(data=marker))
            sample = Twist()
            sample.linear.x = 1.25
            sample.angular.z = -0.5
            vector_pub.publish(sample)
            rclpy.spin_once(node, timeout_sec=0.1)
            if marker in strings and (options.project or any(v.linear.x == 1.25 and v.angular.z == -0.5 for v in vectors)):
                break
        else:
            raise TimeoutError("Expected topic round trips did not complete.")
        process.stdin.write("stop\n")
        process.stdin.flush()
        if process.wait(timeout=15) != 0:
            raise RuntimeError("Consumer shutdown failed.")
        log.seek(0)
        output = log.read()
        if "ROS_TOPICS_STOPPED" not in output:
            raise RuntimeError("Graceful shutdown was not confirmed.")
        print("ROS2_PACKAGE_TOPIC_INTEROP_PASSED=True")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        log.seek(0)
        print(log.read())
        node.destroy_node()
        rclpy.shutdown()
