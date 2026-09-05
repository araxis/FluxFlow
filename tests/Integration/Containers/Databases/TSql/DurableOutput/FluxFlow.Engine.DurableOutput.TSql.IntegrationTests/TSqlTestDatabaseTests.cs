using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

public sealed class TSqlTestDatabaseTests
{
    [Fact]
    public async Task Fresh_database_is_unique_exists_and_cleanup_is_idempotent()
    {
        var first = await TSqlTestDatabase.CreateAsync();
        var second = await TSqlTestDatabase.CreateAsync();
        try
        {
            first.Name.ShouldNotBe(second.Name);
            first.Name.ShouldStartWith("FluxFlowTSqlTests_");
            second.Name.ShouldStartWith("FluxFlowTSqlTests_");
            first.Name.Length.ShouldBe("FluxFlowTSqlTests_".Length + 32);
            first.Name.ShouldAllBe(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '_');
            (await first.ExistsAsync()).ShouldBeTrue();
            (await second.ExistsAsync()).ShouldBeTrue();
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }

        (await first.ExistsAsync()).ShouldBeFalse();
        (await second.ExistsAsync()).ShouldBeFalse();
        await first.DisposeAsync();
        await second.DisposeAsync();
        (await first.ExistsAsync()).ShouldBeFalse();
        (await second.ExistsAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Drop_then_recreate_has_no_static_state_or_connection_leak()
    {
        var envelope = TSqlDurableOutputTestSupport.ValueEnvelope("drop-recreate");
        var first = await TSqlTestDatabase.CreateAsync();
        var firstName = first.Name;
        await using (var store = first.CreateStore())
        {
            (await store.EnqueueAsync(envelope)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
            (await store.ReadAsync(envelope.Key)).ShouldNotBeNull()
                .ShouldMatchExactly(envelope);
        }
        await first.DisposeAsync();
        (await first.ExistsAsync()).ShouldBeFalse();

        var second = await TSqlTestDatabase.CreateAsync();
        try
        {
            second.Name.ShouldNotBe(firstName);
            (await second.ExistsAsync()).ShouldBeTrue();
            await using var connection = await second.OpenConnectionAsync();
            connection.Database.ShouldBe(second.Name);
            connection.State.ShouldBe(System.Data.ConnectionState.Open);
        }
        finally
        {
            await second.DisposeAsync();
        }

        (await second.ExistsAsync()).ShouldBeFalse();
    }
}
