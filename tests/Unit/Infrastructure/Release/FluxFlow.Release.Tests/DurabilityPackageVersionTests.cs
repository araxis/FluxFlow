using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DurabilityPackageVersionTests
{
    public static TheoryData<string, string> ExpectedVersions => new()
    {
        { "src/Engine/DurableInput/FluxFlow.Engine.DurableInput/FluxFlow.Engine.DurableInput.csproj", "3.0.0-rc.2" },
        { "src/Engine/DurableInput/FluxFlow.Engine.DurableInput.SqlFile/FluxFlow.Engine.DurableInput.SqlFile.csproj", "3.0.0-rc.2" },
        { "src/Engine/DurableInput/FluxFlow.Engine.DurableInput.TSql/FluxFlow.Engine.DurableInput.TSql.csproj", "3.0.0-rc.2" },
        { "src/Engine/DurableOutput/FluxFlow.Engine.DurableOutput/FluxFlow.Engine.DurableOutput.csproj", "5.0.0-rc.2" },
        { "src/Engine/DurableOutput/FluxFlow.Engine.DurableOutput.SqlFile/FluxFlow.Engine.DurableOutput.SqlFile.csproj", "5.0.0-rc.2" },
        { "src/Engine/DurableOutput/FluxFlow.Engine.DurableOutput.TSql/FluxFlow.Engine.DurableOutput.TSql.csproj", "4.0.0-rc.2" }
    };

    [Theory]
    [MemberData(nameof(ExpectedVersions))]
    public void Durability_packages_use_the_exact_contract_version(
        string relativeProjectPath,
        string expectedVersion)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(projectPath);
        var versions = project.Descendants("Version").Select(element => element.Value).ToArray();

        versions.ShouldBe([expectedVersion]);
    }
}
