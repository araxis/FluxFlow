using System.Reflection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputRetentionContractTests
{
    private static readonly DateTimeOffset TerminalBefore =
        new(2026, 8, 1, 12, 34, 56, TimeSpan.FromHours(5));

    [Fact]
    public void Request_exposes_exact_defaults_and_retains_supplied_values()
    {
        var defaults = new DurableInputRetentionRequest(TerminalBefore);
        var scoped = new DurableInputRetentionRequest(
            TerminalBefore,
            DurableInputStoreConformanceData.SecondaryInput,
            DurableInputRetentionRequest.MaximumMaxCount);

        DurableInputRetentionRequest.DefaultMaxCount.ShouldBe(100);
        DurableInputRetentionRequest.MaximumMaxCount.ShouldBe(1_000);
        defaults.TerminalBefore.ShouldBe(TerminalBefore);
        defaults.TerminalBefore.Offset.ShouldBe(TerminalBefore.Offset);
        defaults.Address.ShouldBeNull();
        defaults.MaxCount.ShouldBe(DurableInputRetentionRequest.DefaultMaxCount);
        scoped.TerminalBefore.ShouldBe(TerminalBefore);
        scoped.TerminalBefore.Offset.ShouldBe(TerminalBefore.Offset);
        scoped.Address.ShouldBe(DurableInputStoreConformanceData.SecondaryInput);
        scoped.MaxCount.ShouldBe(DurableInputRetentionRequest.MaximumMaxCount);
        scoped.ShouldBe(new DurableInputRetentionRequest(
            TerminalBefore,
            DurableInputStoreConformanceData.SecondaryInput,
            DurableInputRetentionRequest.MaximumMaxCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(DurableInputRetentionRequest.MaximumMaxCount)]
    public void Request_accepts_inclusive_max_count_boundaries(int maxCount)
    {
        new DurableInputRetentionRequest(TerminalBefore, maxCount: maxCount)
            .MaxCount.ShouldBe(maxCount);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(DurableInputRetentionRequest.MaximumMaxCount + 1)]
    [InlineData(int.MaxValue)]
    public void Request_rejects_max_count_outside_inclusive_boundaries(int maxCount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputRetentionRequest(TerminalBefore, maxCount: maxCount))
            .ParamName.ShouldBe("maxCount");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(DurableInputRetentionRequest.MaximumMaxCount)]
    public void Result_accepts_nonnegative_deleted_counts_and_rejects_negative_values(
        int deletedCount)
    {
        var result = new DurableInputRetentionResult(deletedCount);

        result.DeletedCount.ShouldBe(deletedCount);
        result.ShouldBe(new DurableInputRetentionResult(deletedCount));
        Should.Throw<ArgumentOutOfRangeException>(() =>
                new DurableInputRetentionResult(-1))
            .ParamName.ShouldBe("deletedCount");
    }

    [Fact]
    public void Contracts_expose_only_the_exact_immutable_public_shape()
    {
        typeof(DurableInputRetentionRequest).IsSealed.ShouldBeTrue();
        typeof(DurableInputRetentionResult).IsSealed.ShouldBeTrue();
        PublicPropertyNames<DurableInputRetentionRequest>().ShouldBe([
            "TerminalBefore", "Address", "MaxCount"
        ]);
        PublicPropertyNames<DurableInputRetentionResult>().ShouldBe(["DeletedCount"]);
        foreach (var property in typeof(DurableInputRetentionRequest).GetProperties())
            property.SetMethod.ShouldBeNull();
        foreach (var property in typeof(DurableInputRetentionResult).GetProperties())
            property.SetMethod.ShouldBeNull();

        var methods = typeof(IDurableInputRetentionStore)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();
        methods.Select(static method => method.Name).ShouldBe([
            "PurgeDeadLettersAsync", "PurgeDeliveredAsync"
        ]);
        foreach (var method in methods)
        {
            method.ReturnType.ShouldBe(typeof(ValueTask<DurableInputRetentionResult>));
            method.GetParameters().Select(static parameter => parameter.ParameterType).ShouldBe([
                typeof(DurableInputRetentionRequest), typeof(CancellationToken)
            ]);
        }
    }

    private static string[] PublicPropertyNames<T>()
        => typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToArray();
}
