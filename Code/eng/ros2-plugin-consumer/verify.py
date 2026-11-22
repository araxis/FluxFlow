"""Run the installed String plugin against independent localhost ROS endpoints."""
import argparse
import queue
import subprocess
import threading
import time
import uuid

import rclpy
from std_msgs.msg import String

parser = argparse.ArgumentParser()
parser.add_argument("--host", required=True)
parser.add_argument("--store", required=True)
options = parser.parse_args()
prefix = "/fluxflow_test_" + uuid.uuid4().hex
rclpy.init()
node = rclpy.create_node("fluxflow_plugin_peer")
received = []
subscriber = node.create_subscription(String, prefix + "/output", lambda value: received.append(value.data), 10)
publisher = node.create_publisher(String, prefix + "/input", 10)
process = subprocess.Popen([options.host, options.store, prefix], stdin=subprocess.PIPE,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
lines = queue.Queue()


def read_output():
    for line in process.stdout:
        print(line, end="", flush=True)
        lines.put(line.strip())


thread = threading.Thread(target=read_output, daemon=True)
thread.start()


def wait_for(predicate, seconds=25):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        rclpy.spin_once(node, timeout_sec=0.05)
        if predicate():
            return
        if process.poll() is not None:
            raise RuntimeError(f"Host exited early: {process.returncode}")
    raise TimeoutError("ROS trial deadline exceeded")


def marker(expected):
    def found():
        while not lines.empty():
            if lines.get_nowait() == expected:
                return True
        return False
    wait_for(found)


def command(text):
    process.stdin.write(text + "\n")
    process.stdin.flush()


try:
    for run in range(2):
        marker(f"READY {run}")
        wait_for(lambda: publisher.get_subscription_count() == 1 and subscriber.get_publisher_count() == 1)
        received.clear()
        for payload in ["", f'run-{run}-λ\n"hello"', f"run-{run}-🌍"]:
            publisher.publish(String(data=payload))
            wait_for(lambda: payload in received)
        command("stop")
        marker(f"STOPPED {run}")
        wait_for(lambda: publisher.get_subscription_count() == 0 and subscriber.get_publisher_count() == 0)
        received.clear()
        publisher.publish(String(data="stale-while-stopped"))
        until = time.monotonic() + 0.25
        while time.monotonic() < until:
            rclpy.spin_once(node, timeout_sec=0.05)
        if received:
            raise AssertionError("Delivery continued after Stop")
        command("next")
    if process.wait(timeout=15) != 0:
        raise RuntimeError("Host failed after Stop/rerun")
    thread.join(timeout=5)
    print("ROS_PLUGIN_NATIVE_DELIVERY_STOP_RERUN_PASSED")
finally:
    if process.poll() is None:
        process.kill()
        process.wait(timeout=5)
    thread.join(timeout=5)
    node.destroy_node()
    rclpy.shutdown()
