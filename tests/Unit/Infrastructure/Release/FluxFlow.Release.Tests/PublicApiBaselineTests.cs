using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed partial class PublicApiBaselineTests
{
    private const string AcceptBaselineVariable = "FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE";

    [Fact]
    public void Public_api_baseline_matches_current_source_declarations()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var baselinePath = Path.Combine(root, "eng", "public-api", "baseline.txt");
        var actual = BuildBaseline(root);

        if (string.Equals(Environment.GetEnvironmentVariable(AcceptBaselineVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath).ShouldNotBeNull());
            File.WriteAllText(baselinePath, actual);
            return;
        }

        File.Exists(baselinePath)
            .ShouldBeTrue(
                $"public API baseline is missing. Review the public declaration surface, then set {AcceptBaselineVariable}=1 and rerun this test to create it.");

        var expected = File.ReadAllText(baselinePath).ReplaceLineEndings("\n");
        actual.ShouldBe(
            expected,
            $"public source declarations changed. Review whether the change is breaking, additive, or patch-compatible; update package versions and docs as needed; then set {AcceptBaselineVariable}=1 and rerun this test to accept the new baseline.");
    }

    [Fact]
    public void Public_api_baseline_workflow_is_documented()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var versioning = File.ReadAllText(Path.Combine(root, "docs", "11-package-versioning.md"));
        var overview = File.ReadAllText(Path.Combine(root, "docs", "14-public-api-overview.md"));
        var baselineReadme = File.ReadAllText(Path.Combine(root, "eng", "public-api", "README.md"));

        versioning.ShouldContain("## Public API Baseline");
        versioning.ShouldContain(AcceptBaselineVariable);
        overview.ShouldContain("public API baseline");
        baselineReadme.ShouldContain(AcceptBaselineVariable);
        baselineReadme.ShouldContain("source-declaration");
    }

    private static string BuildBaseline(string root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FluxFlow public API source-declaration baseline v1");
        builder.AppendLine("# Entries follow eng/packages.json order.");
        builder.AppendLine("# Format: package-index|public-declaration-count|sha256(normalized declarations)");

        var entries = PackageManifest.Read(root);
        for (var index = 0; index < entries.Count; index++)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entries[index].Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var declarations = ReadPublicSourceDeclarations(projectDirectory);
            var payload = string.Join('\n', declarations);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

            builder.Append(index);
            builder.Append('|');
            builder.Append(declarations.Count);
            builder.Append('|');
            builder.AppendLine(hash);
        }

        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static IReadOnlyList<string> ReadPublicSourceDeclarations(string projectDirectory)
        => Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(projectDirectory, file))
            .Order(StringComparer.Ordinal)
            .SelectMany(file => ReadPublicSourceDeclarations(projectDirectory, file))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadPublicSourceDeclarations(
        string projectDirectory,
        string file)
    {
        var declarations = new List<string>();
        var relativeFile = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
        var lines = File.ReadAllLines(file);
        var inBlockComment = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComments(lines[index], ref inBlockComment).Trim();
            if (!IsPublicDeclarationStart(line))
                continue;

            var declaration = new List<string> { line };
            var parenthesisDepth = CountParentheses(line);
            while (!IsDeclarationComplete(declaration, parenthesisDepth) && index + 1 < lines.Length)
            {
                index++;
                var continuation = StripComments(lines[index], ref inBlockComment).Trim();
                if (continuation.Length == 0 || continuation.StartsWith("[", StringComparison.Ordinal))
                    continue;

                declaration.Add(continuation);
                parenthesisDepth += CountParentheses(continuation);
            }

            var normalized = NormalizeDeclaration(string.Join(' ', declaration));
            if (normalized.Length > 0)
                declarations.Add($"{relativeFile}: {normalized}");
        }

        return declarations;
    }

    private static bool IsBuildOutput(string projectDirectory, string file)
    {
        var relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal);
    }

    private static bool IsPublicDeclarationStart(string line)
    {
        if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal))
            return false;

        return line.StartsWith("public ", StringComparison.Ordinal) ||
               line.StartsWith("protected ", StringComparison.Ordinal);
    }

    private static bool IsDeclarationComplete(
        IReadOnlyList<string> declaration,
        int parenthesisDepth)
    {
        if (parenthesisDepth > 0)
            return false;

        var line = declaration[^1].Trim();
        if (line.StartsWith("where ", StringComparison.Ordinal))
            return false;

        return line.EndsWith(';') ||
               line.EndsWith('{') ||
               line.EndsWith('}') ||
               line.Contains("=>", StringComparison.Ordinal);
    }

    private static string NormalizeDeclaration(string declaration)
    {
        var expressionIndex = declaration.IndexOf("=>", StringComparison.Ordinal);
        if (expressionIndex >= 0)
            declaration = declaration[..expressionIndex] + "=>";

        declaration = WhitespaceRegex().Replace(declaration, " ").Trim();

        if (declaration.EndsWith(" {", StringComparison.Ordinal))
            declaration = declaration[..^2].TrimEnd();

        return declaration.TrimEnd(';');
    }

    private static int CountParentheses(string line)
    {
        var depth = 0;
        foreach (var character in line)
        {
            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;
        }

        return depth;
    }

    private static string StripComments(string line, ref bool inBlockComment)
    {
        var builder = new StringBuilder(line.Length);
        for (var index = 0; index < line.Length; index++)
        {
            if (inBlockComment)
            {
                if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                break;

            builder.Append(line[index]);
        }

        return builder.ToString();
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
