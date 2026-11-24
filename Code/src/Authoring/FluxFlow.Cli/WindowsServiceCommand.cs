using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using System.Xml;

namespace FluxFlow.Cli;

internal static class WindowsServiceCommand
{
    internal static string Usage(string command) =>
        $"Usage: {command} service install <name> [application.json] [--interactive] [--framework net10.0|net11.0] [--feed <folder>] [--startup manual|automatic]\n" +
        $"       {command} service start|stop <name>";

    internal static int Run(string[] args, string command, string? defaultFeed)
    {
        if (args is ["service", "--help" or "-h"] or ["service", "install", "--help" or "-h"])
        {
            Console.WriteLine(Usage(command));
            return 0;
        }
        if (args is not (["service", "install", _, ..]
            or ["service", "start" or "stop", _]))
        {
            Console.Error.WriteLine(Usage(command));
            return 2;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Run this service command on the destination Windows machine.");
            return 1;
        }
        try
        {
            var name = args[2];
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(['/', '\\']) >= 0)
                throw new ArgumentException("Choose a Windows service name of 1 to 80 characters without slashes.");
            if (args[1] == "install")
                return WorkflowServiceInstaller.Run(name, args[3..], defaultFeed, Usage(command));
            using var service = new ServiceController(name, ".");
            return args[1] == "start" ? Start(service) : Stop(service);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException or
            System.ServiceProcess.TimeoutException or System.TimeoutException or IOException or UnauthorizedAccessException
            or InvalidDataException or FormatException or JsonException or XmlException)
        {
            Console.Error.WriteLine(error.Message);
            if (error.InnerException is Win32Exception native) Console.Error.WriteLine(native.Message);
            return 1;
        }
    }

    internal static int Register(string name, string path, bool automatic)
    {
        var executable = Path.GetFullPath(path);
        if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an existing published Windows executable.");
        // Once registration begins, Ctrl+C must not interrupt sc.exe between creating
        // the service and reporting success to the installer that owns its files.
        var create = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "create", name, "binPath=", $"\"{executable}\"", "start=", automatic ? "auto" : "demand",
                     "obj=", @"NT AUTHORITY\LocalService" })
            create.ArgumentList.Add(argument);
        using var process = Process.Start(create)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Console.Write(stdout.GetAwaiter().GetResult());
        Console.Error.Write(stderr.GetAwaiter().GetResult());
        return process.ExitCode == 0 ? 0 : 1;
    }

    [SupportedOSPlatform("windows")]
    private static int Start(ServiceController service)
    {
        if (service.Status == ServiceControllerStatus.Stopped) service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(90));
        Console.WriteLine($"{service.ServiceName}: Running");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int Stop(ServiceController service)
    {
        var status = ReadStatus(service);
        if (status.State != ServiceControllerStatus.Stopped)
        {
            if ((status.Type & ServiceType.Win32OwnProcess) == 0)
                throw new InvalidOperationException("The selected service does not own its process.");
            using var process = Process.GetProcessById(status.ProcessId);
            _ = process.Handle;
            if (status.State != ServiceControllerStatus.StopPending) service.Stop(stopDependentServices: false);
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
            if (!process.WaitForExit(30000))
                throw new InvalidOperationException("The service process is still running. Cleanup is unconfirmed.");
            status = ReadStatus(service);
            if (status.State != ServiceControllerStatus.Stopped)
                throw new InvalidOperationException("The service restarted while Stop was being verified.");
            if (process.ExitCode != 0 && !(process.ExitCode == 2 && status.ExitCode == 1460))
                throw new InvalidOperationException($"Service process exited with code {process.ExitCode}.");
        }
        if (status.ExitCode == 1460)
        {
            Console.Error.WriteLine($"{service.ServiceName}: Stopped. Shutdown incomplete: the workflow exceeded its deadline.");
            return 2;
        }
        if (status.ExitCode != 0) throw new Win32Exception(status.ExitCode);
        Console.WriteLine($"{service.ServiceName}: Stopped");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static NativeStatus ReadStatus(ServiceController service)
    {
        using var handle = service.ServiceHandle;
        if (!QueryServiceStatusEx(handle, 0, out var status, Marshal.SizeOf<NativeStatus>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatus
    {
        internal ServiceType Type;
        internal ServiceControllerStatus State;
        private int _acceptedControls;
        internal int ExitCode;
        private int _specificExitCode, _checkpoint, _waitHint;
        internal int ProcessId;
        private int _flags;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(SafeHandle service, int infoLevel, out NativeStatus status,
        int bufferSize, out int bytesNeeded);
}
