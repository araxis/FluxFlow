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

}
