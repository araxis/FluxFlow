using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace FluxFlow.Cli;

[SupportedOSPlatform("windows")]
internal static class WindowsServiceCommand
{
    internal static int Control(string action, string name)
    {
        if (action == "uninstall") return Uninstall(name);
        using var service = new ServiceController(name, ".");
        return action == "start" ? Start(service) : Stop(service);
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
    private static int Uninstall(string name)
    {
        using var service = new ServiceController { ServiceName = name };
        // Keep this registration alive until Stop and DeleteService have acted on the same service.
        using (var handle = service.ServiceHandle)
        {
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                var result = Stop(service);
                if (result != 0) return result;
            }
            if (!DeleteService(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var elapsed = Stopwatch.StartNew();
        do
        {
            service.Refresh();
            try { _ = service.Status; }
            catch (InvalidOperationException error) when (error.InnerException is Win32Exception { NativeErrorCode: 1060 })
            {
                Console.WriteLine($"{name}: Uninstalled.");
                return 0;
            }
            catch (InvalidOperationException error) when (error.InnerException is Win32Exception { NativeErrorCode: 1072 })
            {
                // SCM can keep the registration until other service handles close.
            }
            Thread.Sleep(100);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
        throw new InvalidOperationException($"Removal of '{name}' is pending. Close any open service handles and check its status again.");
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
    private static extern bool DeleteService(SafeHandle service);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(SafeHandle service, int infoLevel, out NativeStatus status,
        int bufferSize, out int bytesNeeded);
}
