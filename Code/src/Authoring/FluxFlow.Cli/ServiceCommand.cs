using System.ComponentModel;
using System.Text.Json;
using System.Xml;

namespace FluxFlow.Cli;

internal static class ServiceCommand
{
    internal static string Usage(string command) =>
        $"Usage: {command} service install <name> [application.json] [--interactive] [--framework net10.0|net11.0] [--feed <folder>] [--startup manual|automatic]\n" +
        $"       {command} service start|stop|uninstall <name>";

    internal static int Run(string[] args, string command, string? defaultFeed)
    {
        if (args is ["service", "--help" or "-h"] or ["service", "install" or "uninstall", "--help" or "-h"])
        {
            Console.WriteLine(Usage(command));
            return 0;
        }
        if (args is not (["service", "install", _, ..]
            or ["service", "start" or "stop" or "uninstall", _]))
        {
            Console.Error.WriteLine(Usage(command));
            return 2;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Run this service command on the destination Windows or Linux machine.");
            return 1;
        }
        try
        {
            var name = args[2];
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(['/', '\\', '\0']) >= 0)
                throw new ArgumentException("Choose a service name of 1 to 80 characters without slashes.");
            if (OperatingSystem.IsLinux())
            {
                SystemdService.ValidateName(name);
                if (args[1] == "install" && Environment.UserName == "root")
                    throw new InvalidOperationException("Run the installer as an ordinary user. Administrator approval is requested after the workflow is built.");
            }
            if (args[1] == "install")
                return WorkflowServiceInstaller.Run(name, args[3..], defaultFeed, Usage(command));
            return OperatingSystem.IsWindows() ? WindowsServiceCommand.Control(args[1], name)
                : SystemdService.Control(args[1], name);
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
}
