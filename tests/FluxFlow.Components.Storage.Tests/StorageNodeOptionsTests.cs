using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageNodeOptionsTests
{
    [Fact]
    public void Options_normalize_default_collections()
    {
        new StoragePutOptions { Collection = " items " }.Collection.ShouldBe("items");
        new StorageGetOptions { Collection = " items " }.Collection.ShouldBe("items");
        new StorageQueryOptions { Collection = " items " }.Collection.ShouldBe("items");
        new StorageDeleteOptions { Collection = " items " }.Collection.ShouldBe("items");
    }

    [Fact]
    public void Options_treat_blank_default_collections_as_absent()
    {
        new StoragePutOptions { Collection = " " }.Collection.ShouldBeNull();
        new StorageGetOptions { Collection = "\t" }.Collection.ShouldBeNull();
        new StorageQueryOptions { Collection = "\r\n" }.Collection.ShouldBeNull();
        new StorageDeleteOptions { Collection = " " }.Collection.ShouldBeNull();
    }

    [Fact]
    public void Options_reject_non_positive_bounded_capacity()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StoragePutOptions { BoundedCapacity = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StorageGetOptions { BoundedCapacity = -1 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StorageQueryOptions { BoundedCapacity = 0 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StorageDeleteOptions { BoundedCapacity = -1 });
    }

    [Fact]
    public void PutOptions_rejects_unsupported_write_mode()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new StoragePutOptions { Mode = (StorageWriteMode)999 });

    [Fact]
    public void QueryOptions_rejects_invalid_paging()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StorageQueryOptions { Offset = -1 });
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StorageQueryOptions { Limit = 0 });
    }
}
