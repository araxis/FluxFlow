using FluxFlow.Components.Storage.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageRequestContractTests
{
    [Fact]
    public void PutRequest_normalizes_optional_text_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "primary"
        };

        var request = new StoragePutRequest
        {
            Collection = " items ",
            Key = "a",
            Value = "one",
            ContentType = " application/json ",
            Attributes = attributes,
            CorrelationId = " c-1 "
        };
        attributes["tenant"] = "changed";
        attributes["new"] = "value";

        request.Collection.ShouldBe("items");
        request.ContentType.ShouldBe("application/json");
        request.CorrelationId.ShouldBe("c-1");
        request.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        request.Attributes.ShouldContainKey("tenant");
        request.Attributes["tenant"].ShouldBe("primary");
        request.Attributes.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void PutRequest_treats_blank_optional_text_and_null_attributes_as_absent()
    {
        var request = new StoragePutRequest
        {
            Collection = " ",
            Key = "a",
            Value = "one",
            ContentType = "\t",
            Attributes = null!,
            CorrelationId = " "
        };

        request.Collection.ShouldBeNull();
        request.ContentType.ShouldBeNull();
        request.CorrelationId.ShouldBeNull();
        request.Attributes.ShouldBeEmpty();
        request.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    [Fact]
    public void QueryRequest_normalizes_filters_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "alpha"
        };

        var request = new StorageQueryRequest
        {
            Collection = " items ",
            KeyPrefix = " user: ",
            Attributes = attributes,
            CorrelationId = " q-1 "
        };
        attributes["kind"] = "changed";

        request.Collection.ShouldBe("items");
        request.KeyPrefix.ShouldBe("user:");
        request.CorrelationId.ShouldBe("q-1");
        request.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
        request.Attributes["kind"].ShouldBe("alpha");
    }

    [Fact]
    public void QueryRequest_treats_blank_filters_and_null_attributes_as_absent()
    {
        var request = new StorageQueryRequest
        {
            Collection = "\t",
            KeyPrefix = " ",
            Attributes = null!,
            CorrelationId = "\r\n"
        };

        request.Collection.ShouldBeNull();
        request.KeyPrefix.ShouldBeNull();
        request.CorrelationId.ShouldBeNull();
        request.Attributes.ShouldBeEmpty();
        request.Attributes.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    [Fact]
    public void ReadRequests_normalize_optional_text()
    {
        var get = new StorageGetRequest
        {
            Collection = " items ",
            Key = "a",
            CorrelationId = " c-get "
        };
        var delete = new StorageDeleteRequest
        {
            Collection = " items ",
            Key = "a",
            CorrelationId = " c-delete "
        };

        get.Collection.ShouldBe("items");
        get.CorrelationId.ShouldBe("c-get");
        delete.Collection.ShouldBe("items");
        delete.CorrelationId.ShouldBe("c-delete");
    }
}
