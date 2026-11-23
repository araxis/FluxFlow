#:property TargetFramework=net10.0
#:property RollForward=Major
#:property PublishAot=false
#:property PublishTrimmed=false
#:property PublishSingleFile=true

using System.Diagnostics;

var command = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    ArgumentList = { "tool", "run", "dotnet-fluxflow", "--" }
};
foreach (var argument in args) command.ArgumentList.Add(argument);

// The child shares the console and needs time to cancel the build and clear its workspace.
Console.CancelKeyPress += (_, e) => e.Cancel = true;
using var tool = Process.Start(command)!;
tool.WaitForExit();
return tool.ExitCode;
