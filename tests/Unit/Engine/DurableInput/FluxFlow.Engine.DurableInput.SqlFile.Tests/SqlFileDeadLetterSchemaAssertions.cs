using Microsoft.Data.Sqlite;
using Shouldly;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

internal static class SqlFileDeadLetterSchemaAssertions
{
    public static async ValueTask ShouldHaveExactVersionTwoShapeAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();

        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.CommandText = "PRAGMA table_info('fluxflow_durable_inputs');";
            await using var reader = await columnCommand.ExecuteReaderAsync();
            var columns = new List<(string Name, string Type, bool NotNull, string? Default)>();
            while (await reader.ReadAsync())
            {
                columns.Add((
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            columns.Single(static column => column.Name == "dead_letter_generation")
                .ShouldBe(("dead_letter_generation", "INTEGER", true, "0"));
        }

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "PRAGMA index_xinfo('ix_fluxflow_durable_inputs_dead_lettered');";
            await using var reader = await indexCommand.ExecuteReaderAsync();
            var keys = new List<(string Name, bool Descending)>();
            while (await reader.ReadAsync())
            {
                if (reader.GetInt32(5) == 1)
                {
                    keys.Add((reader.GetString(2), reader.GetInt32(3) == 1));
                }
            }

            keys.ShouldBe([
                ("state", false),
                ("dead_lettered_at_utc_ticks", true),
                ("application_address", false),
                ("message_id", false)
            ]);
        }

        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = """
            SELECT sql
            FROM sqlite_schema
            WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';
            """;
        var sql = (string?)await sqlCommand.ExecuteScalarAsync();
        sql.ShouldNotBeNull();
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ShouldContain("WHERE state = 3");
    }
}
