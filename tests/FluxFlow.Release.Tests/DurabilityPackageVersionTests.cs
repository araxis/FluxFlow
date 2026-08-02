using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class DurabilityPackageVersionTests
{
    public static TheoryData<string, string> ExpectedVersions => new()
    {
        { "src/FluxFlow.Engine.DurableInput/FluxFlow.Engine.DurableInput.csproj", "1.3.0" },
        { "src/FluxFlow.Engine.DurableInput.SqlFile/FluxFlow.Engine.DurableInput.SqlFile.csproj", "1.3.0" },
        { "src/FluxFlow.Engine.DurableInput.TSql/FluxFlow.Engine.DurableInput.TSql.csproj", "1.2.0" },
        { "src/FluxFlow.Engine.DurableOutput/FluxFlow.Engine.DurableOutput.csproj", "3.0.0" },
        { "src/FluxFlow.Engine.DurableOutput.SqlFile/FluxFlow.Engine.DurableOutput.SqlFile.csproj", "3.0.0" },
        { "src/FluxFlow.Engine.DurableOutput.TSql/FluxFlow.Engine.DurableOutput.TSql.csproj", "2.0.0" }
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
