#:project ../src/Authoring/FluxFlow.Cli/FluxFlow.Cli.csproj
#:property PublishAot=false

return FluxFlow.Cli.Program.Main(["generate", .. args]);
