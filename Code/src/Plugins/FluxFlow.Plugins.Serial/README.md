# Serial plugin

Adds Serial Text, Serial Reader, Serial Writer, port discovery and the workflow-local Serial port connection to FluxFlow. Existing type names, settings, links and binary payloads are unchanged. The package supplies the runtime, port driver assets, configuration, documentation, discovery operation, and portable property definitions. Installing or removing it takes effect after restarting the companion and reloading Studio. See the plugin guide for local installation.

In the solution, `Runtime` owns device I/O, `Settings` owns the driver-independent port and frame options, and `Shared/Design` supplies visibility and validation rules. The port-name field declares a suggestions operation and accepts manual input. Hosts render these definitions with their own controls; the package contains no UI assembly. Each source file belongs to one project.

Upgrade from the original host-backed pilot by removing the old Serial package before starting the updated host. Use a host with the portable design contract (SDK 1.0.0-alpha.2 or later). Install version 1.0.0-alpha.5, restart the companion, and reload Studio. Removing a package does not edit project files.
