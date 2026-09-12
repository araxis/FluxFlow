using FluxFlow.Engine.Benchmarks.LoadQualification;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

public sealed class LoadOptionsTests
{
    [Theory]
    [InlineData(false, 300)]
    [InlineData(true, 716)]
    public void Tracking_bound_includes_prepared_revision_outputs(bool updates, int expected)
    {
        // Per update hop: stable input, prepared output staging, stable output dispatch.
        new LoadOptions { Updates = updates }.InFlightBound.ShouldBe(expected);
    }

    [Theory]
    [InlineData("--runtime", "unknown")]
    [InlineData("--tracing", "unknown")]
    [InlineData("--payload-kind", "unknown")]
    [InlineData("--hops", "0")]
    [InlineData("--fanout", "9")]
    [InlineData("--seconds", "121")]
    [InlineData("--capacity", "0")]
    [InlineData("--unknown", "1")]
    public void Load_options_reject_unknown_or_invalid_values(string key, string value)
        => Should.Throw<ArgumentException>(() => LoadOptions.Parse([key, value, "--output", "report.json"]));

    [Fact]
    public void Load_options_preserve_explicit_values_and_reject_ambiguous_pairs()
    {
        var (options, output) = LoadOptions.Parse(["--runtime", "updates", "--messages", "128", "--output", "report.json"]);
        options.Updates.ShouldBeTrue();
        options.MessageLimit.ShouldBe(128);
        options.Hops.ShouldBe(8);
        options.PayloadKind.ShouldBe("json");
        output.ShouldBe("report.json");
        Should.Throw<ArgumentException>(() => LoadOptions.Parse(["--output"]));
        Should.Throw<ArgumentException>(() => LoadOptions.Parse([]));
        Should.Throw<ArgumentException>(() => LoadOptions.Parse(["--output", "a.json", "--output", "b.json"]));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("binary")]
    public void Payload_kind_and_size_are_explicit_command_options(string kind)
    {
        var (options, output) = LoadOptions.Parse(["--payload-kind", kind, "--payload-bytes", "65536", "--output", "payload.json"]);
        options.PayloadKind.ShouldBe(kind);
        options.PayloadBytes.ShouldBe(65536);
        options.Updates.ShouldBeFalse();
        output.ShouldBe("payload.json");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("unsampled")]
    [InlineData("propagation")]
    [InlineData("recorded")]
    public void Tracing_mode_and_input_context_are_explicit_load_options(string mode)
    {
        var (options, output) = LoadOptions.Parse(["--tracing", mode, "--input-trace-context", "true", "--output", "trace.json"]);
        options.Tracing.ShouldBe(mode);
        options.InputTraceContext.ShouldBeTrue();
        output.ShouldBe("trace.json");
        var (defaults, _) = LoadOptions.Parse(["--output", "default.json"]);
        defaults.Tracing.ShouldBe("off");
        defaults.InputTraceContext.ShouldBeFalse();
    }
}
