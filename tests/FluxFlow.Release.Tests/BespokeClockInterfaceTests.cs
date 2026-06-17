using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class BespokeClockInterfaceTests
{
    private static readonly Regex BespokeClockPattern =
        new(@"\binterface\s+I[A-Za-z]+Clock\b", RegexOptions.Compiled);

    [Fact]
    public void Source_components_do_not_declare_bespoke_clock_interfaces()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var src = Path.Combine(root, "src");

        var scanRoots = Directory
            .EnumerateDirectories(src, "FluxFlow.Components.*", SearchOption.TopDirectoryOnly)
            .Append(Path.Combine(src, "FluxFlow.Engine"))
            .Where(Directory.Exists);

        var offenders = scanRoots
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => BespokeClockPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "src component packages must use System.TimeProvider, not a bespoke per-package clock interface. " +
            $"Offending files: {string.Join(", ", offenders)}");
    }
}
