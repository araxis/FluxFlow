using FluxFlow.Configuration.Expressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class ExpressionSourceTests
{
    [Theory]
    [InlineData("jsonata", "map.cs")]
    [InlineData("JSONATA", "file:map.CSX")]
    [InlineData("csharp", "./map.jsonata")]
    public void Known_engine_mismatch_is_reported_before_reading_the_file(string engine, string file)
    {
        var error = Should.Throw<ArgumentException>(() => new ExpressionSource(file).Read(AppContext.BaseDirectory, engine));
        error.Message.ShouldContain("incompatible");
        error.Message.ShouldContain(engine);
    }

    [Theory]
    [InlineData("csharp", "map.cs")]
    [InlineData("csharp", "map.csx")]
    [InlineData("JSONATA", "map.JSONATA")]
    [InlineData("custom", "map.cs")]
    [InlineData("csharp", "map.txt")]
    [InlineData("jsonata", "map")]
    public void Matching_custom_and_unspecified_file_types_remain_supported(string engine, string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-expression-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name);
        try
        {
            File.WriteAllText(file, "true");
            new ExpressionSource(file).Read(directory, engine).ShouldBe("true");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("inline:value.cs", "jsonata", "value.cs")]
    [InlineData("inline:value.jsonata", "csharp", "value.jsonata")]
    [InlineData("base64:dmFsdWUuY3M=", "jsonata", "value.cs")]
    public void Explicit_text_is_not_validated_as_a_filename(string source, string engine, string expected)
        => new ExpressionSource(source).Read(AppContext.BaseDirectory, engine).ShouldBe(expected);

    [Theory]
    [InlineData("value.price / value.quantity")]
    [InlineData("price/quantity")]
    [InlineData("value.folder.name")]
    [InlineData("'maps/test.jsonata'")]
    [InlineData("\"C:\\maps\\test.csx\"")]
    public void Inline_expressions_are_not_mistaken_for_paths(string expression)
        => new ExpressionSource(expression).Read(AppContext.BaseDirectory).ShouldBe(expression);

    [Theory]
    [InlineData("expression")]
    [InlineData("expression.txt")]
    [InlineData("with spaces.jsonata")]
    public void Relative_and_absolute_paths_need_no_source_prefix(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-expression-path-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        var file = Path.Combine(directory, name);
        try
        {
            File.WriteAllText(file, "value.number + 1");
            new ExpressionSource("./" + name).Read(directory).ShouldBe("value.number + 1");
            new ExpressionSource("../" + name).Read(child).ShouldBe("value.number + 1");
            new ExpressionSource(file).Read(child).ShouldBe("value.number + 1");
            new ExpressionSource("inline:./" + name).Read(directory).ShouldBe("./" + name);
            if (OperatingSystem.IsWindows())
                new ExpressionSource(@".\" + name).Read(directory).ShouldBe("value.number + 1");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(child);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("./missing-expression")]
    [InlineData("./missing-expression.txt")]
    [InlineData("missing-expression.jsonata")]
    public void Missing_expression_files_do_not_fall_back_to_inline_text(string path)
        => Should.Throw<FileNotFoundException>(() => new ExpressionSource(path).Read(AppContext.BaseDirectory));
}
