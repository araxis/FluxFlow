using FluxFlow.Components.Storage.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageOutputContractTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StorageRecord_normalizes_text_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "primary"
        };

        var record = new StorageRecord
        {
            Collection = " items ",
            Key = " a ",
            Value = "one",
            ContentType = " text/plain ",
            Attributes = attributes,
            StoredAt = Timestamp,
            CorrelationId = " c-1 "
        };
        attributes["tenant"] = "changed";

        record.Collection.ShouldBe("items");
        record.Key.ShouldBe("a");
        record.ContentType.ShouldBe("text/plain");
        record.CorrelationId.ShouldBe("c-1");
        record.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void StorageRecord_treats_blank_optional_text_and_null_attributes_as_absent()
    {
        var record = new StorageRecord
        {
            Collection = " items ",
            Key = " a ",
            ContentType = " ",
            Attributes = null!,
            StoredAt = Timestamp,
            CorrelationId = "\t"
        };

        record.ContentType.ShouldBeNull();
        record.CorrelationId.ShouldBeNull();
        record.Attributes.ShouldBeEmpty();
        record.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    [Fact]
    public void StorageResult_normalizes_text_and_copies_attributes_and_record()
    {
        var resultAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["result"] = "yes"
        };
        var recordAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["record"] = "yes"
        };
        var record = new StorageRecord
        {
            Collection = " items ",
            Key = " a ",
            Attributes = recordAttributes,
            StoredAt = Timestamp,
            CorrelationId = " r-1 "
        };

        var result = new StorageResult
        {
            Timestamp = Timestamp,
            Operation = " put ",
            Collection = " items ",
            Key = " a ",
            Succeeded = true,
            Found = true,
            Record = record,
            Version = 1,
            Message = " stored ",
            CorrelationId = " c-1 ",
            Attributes = resultAttributes
        };
        resultAttributes["result"] = "changed";
        record.Attributes["record"] = "changed";

        result.Operation.ShouldBe("put");
        result.Collection.ShouldBe("items");
        result.Key.ShouldBe("a");
        result.Message.ShouldBe("stored");
        result.CorrelationId.ShouldBe("c-1");
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Attributes["result"].ShouldBe("yes");
        result.Record.ShouldNotBeNull();
        result.Record.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Record.Attributes["record"].ShouldBe("yes");
    }

    [Fact]
    public void StorageResult_treats_blank_optional_text_and_null_attributes_as_absent()
    {
        var result = new StorageResult
        {
            Timestamp = Timestamp,
            Operation = " get ",
            Collection = " items ",
            Key = " a ",
            Succeeded = true,
            Message = "\r\n",
            CorrelationId = " ",
            Attributes = null!
        };

        result.Message.ShouldBeNull();
        result.CorrelationId.ShouldBeNull();
        result.Attributes.ShouldBeEmpty();
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    [Fact]
    public void StorageQueryResult_normalizes_text_and_copies_attributes_and_records()
    {
        var resultAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = "yes"
        };
        var record = new StorageRecord
        {
            Collection = " items ",
            Key = " a ",
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["record"] = "yes"
            },
            StoredAt = Timestamp,
            CorrelationId = " r-1 "
        };
        var records = new List<StorageRecord> { record };

        var result = new StorageQueryResult
        {
            Timestamp = Timestamp,
            Operation = " query ",
            Collection = " items ",
            Succeeded = true,
            Count = 1,
            Records = records,
            Message = " complete ",
            CorrelationId = " q-1 ",
            Attributes = resultAttributes
        };
        records.Add(record with { Key = "b" });
        record.Attributes["record"] = "changed";
        resultAttributes["query"] = "changed";

        result.Operation.ShouldBe("query");
        result.Collection.ShouldBe("items");
        result.Message.ShouldBe("complete");
        result.CorrelationId.ShouldBe("q-1");
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Attributes["query"].ShouldBe("yes");
        result.Records.Count.ShouldBe(1);
        result.Records[0].Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Records[0].Attributes["record"].ShouldBe("yes");
    }

    [Fact]
    public void StorageQueryResult_treats_blank_optional_text_null_records_and_null_attributes_as_absent()
    {
        var result = new StorageQueryResult
        {
            Timestamp = Timestamp,
            Operation = " query ",
            Collection = " items ",
            Succeeded = true,
            Count = 0,
            Records = null!,
            Message = " ",
            CorrelationId = "\t",
            Attributes = null!
        };

        result.Records.ShouldBeEmpty();
        result.Message.ShouldBeNull();
        result.CorrelationId.ShouldBeNull();
        result.Attributes.ShouldBeEmpty();
        result.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }
}
