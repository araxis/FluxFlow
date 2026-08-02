using System.Reflection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputRetentionContractTests
{
    private static readonly DateTimeOffset TerminalBefore =
        new(2026, 8, 1, 12, 34, 56, TimeSpan.FromHours(-4));

    [Fact]
    public void Request_exposes_exact_defaults_and_retains_supplied_values()
    {
        var defaults = new DurableOutputRetentionRequest(TerminalBefore);
        var scoped = new DurableOutputRetentionRequest(
            TerminalBefore,
            DurableOutputStoreConformanceData.SecondaryOutput,
            DurableOutputRetentionRequest.MaximumMaxCount);

        DurableOutputRetentionRequest.DefaultMaxCount.ShouldBe(100);
        DurableOutputRetentionRequest.MaximumMaxCount.ShouldBe(1_000);
        defaults.TerminalBefore.ShouldBe(TerminalBefore);
        defaults.TerminalBefore.Offset.ShouldBe(TerminalBefore.Offset);
        defaults.Address.ShouldBeNull();
        defaults.MaxCount.ShouldBe(DurableOutputRetentionRequest.DefaultMaxCount);
        scoped.TerminalBefore.ShouldBe(TerminalBefore);
        scoped.TerminalBefore.Offset.ShouldBe(TerminalBefore.Offset);
        scoped.Address.ShouldBe(DurableOutputStoreConformanceData.SecondaryOutput);
        scoped.MaxCount.ShouldBe(DurableOutputRetentionRequest.MaximumMaxCount);
        scoped.ShouldBe(new DurableOutputRetentionRequest(
            TerminalBefore,
            DurableOutputStoreConformanceData.SecondaryOutput,
            DurableOutputRetentionRequest.MaximumMaxCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(DurableOutputRetentionRequest.MaximumMaxCount)]
    public void Request_accepts_inclusive_max_count_boundaries(int maxCount)
    {
        new DurableOutputRetentionRequest(TerminalBefore, maxCount: maxCount)
            .MaxCount.ShouldBe(maxCount);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(DurableOutputRetentionRequest.MaximumMaxCount + 1)]
    [InlineData(int.MaxValue)]
    public void Request_rejects_max_count_outside_inclusive_boundaries(int maxCount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputRetentionRequest(TerminalBefore, maxCount: maxCount))
            .ParamName.ShouldBe("maxCount");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(DurableOutputRetentionRequest.MaximumMaxCount)]
    public void Result_accepts_nonnegative_deleted_counts_and_rejects_negative_values(
        int deletedCount)
    {
        var result = new DurableOutputRetentionResult(deletedCount);

        result.DeletedCount.ShouldBe(deletedCount);
        result.ShouldBe(new DurableOutputRetentionResult(deletedCount));
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableOutputRetentionResult(-1))
            .ParamName.ShouldBe("deletedCount");
    }

    [Fact]
    public void Contracts_expose_only_the_exact_immutable_public_shape()
    {
        typeof(DurableOutputRetentionRequest).IsSealed.ShouldBeTrue();
        typeof(DurableOutputRetentionResult).IsSealed.ShouldBeTrue();
        PublicPropertyNames<DurableOutputRetentionRequest>().ShouldBe([
            "TerminalBefore", "Address", "MaxCount"
        ]);
        PublicPropertyNames<DurableOutputRetentionResult>().ShouldBe(["DeletedCount"]);
        foreach (var property in typeof(DurableOutputRetentionRequest).GetProperties())
            property.SetMethod.ShouldBeNull();
        foreach (var property in typeof(DurableOutputRetentionResult).GetProperties())
            property.SetMethod.ShouldBeNull();

        var methods = typeof(IDurableOutputRetentionStore)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();
        methods.Select(static method => method.Name).ShouldBe([
            "PurgeCompletedAsync", "PurgeDeadLettersAsync"
        ]);
        foreach (var method in methods)
        {
            method.ReturnType.ShouldBe(typeof(ValueTask<DurableOutputRetentionResult>));
            method.GetParameters().Select(static parameter => parameter.ParameterType).ShouldBe([
                typeof(DurableOutputRetentionRequest), typeof(CancellationToken)
            ]);
        }
    }

    private static string[] PublicPropertyNames<T>()
        => typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToArray();
}
