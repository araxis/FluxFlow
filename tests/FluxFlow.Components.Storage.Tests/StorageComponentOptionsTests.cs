using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

public sealed class StorageComponentOptionsTests
{
    [Fact]
    public async Task UseStore_rejects_null_lease_from_delegate()
    {
        var options = new StorageComponentOptions()
            .UseStore((_, _) => ValueTask.FromResult<StorageStoreLease>(null!));

        var act = async () => await options.StoreFactory.OpenAsync(new StorageStoreContext());

        var exception = await act.ShouldThrowAsync<InvalidOperationException>();
        exception.Message.ShouldBe("Storage store factory delegate returned a null lease.");
    }

    [Fact]
    public async Task UseSharedStore_rejects_null_store_from_delegate()
    {
        var options = new StorageComponentOptions()
            .UseSharedStore(_ => null!);

        var act = async () => await options.StoreFactory.OpenAsync(new StorageStoreContext());

        var exception = await act.ShouldThrowAsync<InvalidOperationException>();
        exception.Message.ShouldBe("Shared storage store factory returned null.");
    }

    [Fact]
    public async Task UseStore_rejects_null_context_before_invoking_delegate()
    {
        var invoked = false;
        var options = new StorageComponentOptions()
            .UseStore((_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(StorageStoreLease.Shared(new InMemoryStorageStore()));
            });

        var act = async () => await options.StoreFactory.OpenAsync(null!);

        await act.ShouldThrowAsync<ArgumentNullException>();
        invoked.ShouldBeFalse();
    }
}
