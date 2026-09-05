using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;
using Shouldly;
using System.Text.Json;
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
        record.Attributes["tenant"].ShouldBe("primary");
        record.Attributes.ContainsKey("Tenant").ShouldBeFalse();
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, string>)record.Attributes)["tenant"] = "mutated");
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
        recordAttributes["record"] = "changed";

        result.Operation.ShouldBe("put");
        result.Collection.ShouldBe("items");
        result.Key.ShouldBe("a");
        result.Message.ShouldBe("stored");
        result.CorrelationId.ShouldBe("c-1");
        result.Attributes["result"].ShouldBe("yes");
        result.Attributes.ContainsKey("Result").ShouldBeFalse();
        result.Record.ShouldNotBeNull();
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
    }

    [Fact]
    public void Attributes_contracts_are_ordinal_read_only_snapshots_and_round_trip()
    {
        var cases = new[]
        {
            ContractCase(attributes => new StoragePutRequest
            {
                Key = "key",
                Attributes = attributes
            }),
            ContractCase(attributes => new StorageContentPutRequest
            {
                Key = "key",
                Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
                Attributes = attributes
            }),
            ContractCase(attributes => new StorageQueryRequest
            {
                Attributes = attributes
            }),
            ContractCase(attributes => new StorageRecord
            {
                Collection = "items",
                Key = "key",
                StoredAt = Timestamp,
                Attributes = attributes
            }),
            ContractCase(attributes => new StorageContentRecord
            {
                Collection = "items",
                Key = "key",
                Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
                StoredAt = Timestamp,
                Attributes = attributes
            }),
            ContractCase(attributes => new StorageResult
            {
                Timestamp = Timestamp,
                Operation = "put",
                Collection = "items",
                Key = "key",
                Succeeded = true,
                Attributes = attributes
            })
        };

        foreach (var (contract, source) in cases)
        {
            source["Tenant"] = "changed";
            source["Later"] = "ignored";
            var attributes = GetAttributes(contract);

            attributes["Tenant"].ShouldBe("primary", contract.GetType().Name);
            attributes.ContainsKey("tenant").ShouldBeFalse(contract.GetType().Name);
            attributes.ContainsKey("Later").ShouldBeFalse(contract.GetType().Name);
            Should.Throw<NotSupportedException>(() =>
                ((IDictionary<string, string>)attributes)["Tenant"] = "mutated");

            var json = JsonSerializer.Serialize(contract, contract.GetType());
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("Attributes").GetProperty("Tenant")
                .GetString().ShouldBe("primary", contract.GetType().Name);
            var roundTrip = JsonSerializer.Deserialize(json, contract.GetType());
            roundTrip.ShouldNotBeNull(contract.GetType().Name);
            var roundTripAttributes = GetAttributes(roundTrip);
            roundTripAttributes["Tenant"].ShouldBe("primary", contract.GetType().Name);
            roundTripAttributes.ContainsKey("tenant").ShouldBeFalse(contract.GetType().Name);
            Should.Throw<NotSupportedException>(() =>
                ((IDictionary<string, string>)roundTripAttributes)["Tenant"] = "mutated");
        }
    }

    private static (object Contract, Dictionary<string, string> Source) ContractCase(
        Func<Dictionary<string, string>, object> create)
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tenant"] = "primary"
        };
        return (create(source), source);
    }

    private static IReadOnlyDictionary<string, string> GetAttributes(object contract)
        => (IReadOnlyDictionary<string, string>)contract.GetType()
            .GetProperty("Attributes")!
            .GetValue(contract)!;

}
