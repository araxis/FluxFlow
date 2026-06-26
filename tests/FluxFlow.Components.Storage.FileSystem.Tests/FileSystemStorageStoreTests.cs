using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.FileSystem.Tests;

public sealed class FileSystemStorageStoreTests
{
    [Fact]
    public async Task Put_PersistsRecordAcrossStoreInstances()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);

        var saved = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first",
            ContentType = "text/plain",
            Attributes = new Dictionary<string, string>
            {
                ["source"] = "test"
            },
            CorrelationId = "c-1"
        });

        var reopened = CreateStore(temp.Path);
        var loaded = await reopened.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        saved.Version.ShouldBe(1);
        loaded.ShouldNotBeNull();
        loaded.Value.ShouldBe("first");
        loaded.ContentType.ShouldBe("text/plain");
        loaded.Attributes["source"].ShouldBe("test");
        loaded.CorrelationId.ShouldBe("c-1");
    }

    [Fact]
    public async Task PutAndDelete_UseConfiguredClock()
    {
        var now = new DateTimeOffset(2026, 2, 3, 5, 1, 2, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path, clock);

        var saved = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });
        var deleted = await store.DeleteAsync(new StorageDeleteRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        saved.StoredAt.ShouldBe(now);
        deleted.Timestamp.ShouldBe(now);
    }

    [Fact]
    public async Task Put_HonorsWriteModesAndExpectedVersion()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);

        var created = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "one",
            Mode = StorageWriteMode.Create
        });
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "duplicate",
            Mode = StorageWriteMode.Create
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "missing",
            Value = "missing",
            Mode = StorageWriteMode.Replace
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "bad-version",
            ExpectedVersion = created.Version + 1
        }));

        var replaced = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "two",
            Mode = StorageWriteMode.Replace,
            ExpectedVersion = created.Version
        });

        replaced.Version.ShouldBe(2);
        replaced.Value.ShouldBe("two");
    }

    [Fact]
    public async Task Put_RejectsUnsupportedWriteMode()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => store.PutAsync(new StoragePutRequest
            {
                Collection = "items",
                Key = "alpha",
                Value = "one",
                Mode = (StorageWriteMode)999
            }));

        exception.Message.ShouldContain("write mode");
    }

    [Fact]
    public async Task Get_HonorsExpiration()
    {
        var now = new DateTimeOffset(2026, 2, 3, 5, 2, 3, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path, clock);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "expired",
            Value = "old",
            ExpiresAt = now.AddMinutes(-1)
        });

        var current = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "expired"
        });
        var expired = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "expired",
            IncludeExpired = true
        });

        current.ShouldBeNull();
        expired.ShouldNotBeNull();
        expired.Value.ShouldBe("old");
    }

    [Fact]
    public async Task Query_FiltersRecordsAndHonorsLimit()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-1",
            Value = "one",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-2",
            Value = "two",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "b-1",
            Value = "three",
            Attributes = new Dictionary<string, string> { ["kind"] = "beta" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "other",
            Key = "a-3",
            Value = "other",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });

        var records = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            KeyPrefix = "a-",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" },
            Limit = 1
        });

        records.ShouldHaveSingleItem().Key.ShouldBe("a-1");
    }

    [Fact]
    public async Task Put_NormalizesAttributesAndQueryMatchesNormalizedAttributes()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "one",
            Attributes = new Dictionary<string, string>
            {
                [" tenant "] = " primary "
            }
        });

        var loaded = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        });
        var records = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            Attributes = new Dictionary<string, string>
            {
                [" tenant "] = " primary "
            }
        });

        loaded.ShouldNotBeNull();
        loaded.Attributes.ContainsKey("tenant").ShouldBeTrue();
        loaded.Attributes["tenant"].ShouldBe("primary");
        records.ShouldHaveSingleItem().Key.ShouldBe("alpha");
    }

    [Fact]
    public async Task Put_RejectsInvalidAttributes()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);

        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "blank-key",
            Value = "one",
            Attributes = new Dictionary<string, string>
            {
                [" "] = "primary"
            }
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "blank-value",
            Value = "one",
            Attributes = new Dictionary<string, string>
            {
                ["tenant"] = " "
            }
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "duplicate-key",
            Value = "one",
            Attributes = new Dictionary<string, string>
            {
                [" tenant "] = "primary",
                ["tenant"] = "secondary"
            }
        }));
    }

    [Fact]
    public async Task Query_HonorsExpiration()
    {
        var now = new DateTimeOffset(2026, 2, 3, 5, 3, 4, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path, clock);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "expired",
            Value = "old",
            ExpiresAt = now.AddMinutes(-1)
        });

        var current = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items"
        });
        var expired = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            IncludeExpired = true
        });

        current.ShouldBeEmpty();
        expired.ShouldHaveSingleItem().Key.ShouldBe("expired");
    }

    [Fact]
    public async Task Query_UsesSingleClockTimestampForExpirationFiltering()
    {
        var now = new DateTimeOffset(2026, 2, 3, 5, 4, 5, TimeSpan.Zero);
        using var temp = TempDirectory.Create();
        var writer = CreateStore(temp.Path, new FakeTimeProvider(now));
        await writer.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first",
            ExpiresAt = now.AddSeconds(1)
        });
        await writer.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "beta",
            Value = "second",
            ExpiresAt = now.AddSeconds(1)
        });
        var reader = CreateStore(
            temp.Path,
            new AdvancingTimeProvider(now, TimeSpan.FromSeconds(2)));

        var records = await reader.QueryAsync(new StorageQueryRequest
        {
            Collection = "items"
        });

        records.Select(record => record.Key).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Query_HonorsOffset()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-1",
            Value = "one"
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-2",
            Value = "two"
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-3",
            Value = "three"
        });

        var records = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            Offset = 1,
            Limit = 1
        });

        records.ShouldHaveSingleItem().Key.ShouldBe("a-2");
    }

    [Fact]
    public async Task Query_RejectsInvalidPaging()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);

        var offset = await Should.ThrowAsync<InvalidOperationException>(() => store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            Offset = -1
        }));
        var limit = await Should.ThrowAsync<InvalidOperationException>(() => store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            Limit = 0
        }));
        var range = await Should.ThrowAsync<InvalidOperationException>(() => store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            StoredFrom = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            StoredTo = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }));

        offset.Message.ShouldContain("offset");
        limit.Message.ShouldContain("limit");
        range.Message.ShouldContain("storedFrom");
    }

    [Fact]
    public async Task Delete_ReturnsFoundAndMissingResults()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });

        var deleted = await store.DeleteAsync(new StorageDeleteRequest
        {
            Collection = "items",
            Key = "alpha",
            CorrelationId = "delete-alpha"
        });
        var missing = await store.DeleteAsync(new StorageDeleteRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        deleted.Found.ShouldBeTrue();
        deleted.Deleted.ShouldBeTrue();
        deleted.Record.ShouldNotBeNull();
        deleted.CorrelationId.ShouldBe("delete-alpha");
        missing.Found.ShouldBeFalse();
        missing.Deleted.ShouldBeFalse();
        missing.Record.ShouldBeNull();
    }

    [Fact]
    public async Task Store_UsesSafePathsForCollectionAndKey()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        var collection = "../orders\\tenant";
        var key = "a:b?c/d";

        await store.PutAsync(new StoragePutRequest
        {
            Collection = collection,
            Key = key,
            Value = "safe"
        });

        var loaded = await store.GetAsync(new StorageGetRequest
        {
            Collection = collection,
            Key = key
        });
        var file = Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();

        loaded.ShouldNotBeNull();
        loaded.Value.ShouldBe("safe");
        file.ShouldNotContain("orders");
        file.ShouldNotContain("tenant");
        file.ShouldNotContain("a:b");
        file.ShouldNotContain("c/d");
    }

    [Fact]
    public async Task Put_RejectsValuesAboveConfiguredLimit()
    {
        using var temp = TempDirectory.Create();
        var store = new FileSystemStorageStore(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path,
            MaxValueBytes = 5
        });

        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "large",
            Value = "abcdef"
        }));
    }

    [Fact]
    public async Task Factory_UsesContextDefaultsAndCreatesSharedLease()
    {
        using var temp = TempDirectory.Create();
        var factory = new FileSystemStorageStoreFactory(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path
        });

        await using var lease = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "tenant-a",
            Collection = "items"
        });
        var saved = await lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        lease.OwnsStore.ShouldBeFalse();
        saved.Collection.ShouldBe("items");
        Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Service_registration_can_register_keyed_store_directly()
    {
        using var temp = TempDirectory.Create();
        var services = new ServiceCollection()
            .AddFluxFlowFileSystemStorageStore(
                "items-store",
                new FileSystemStorageStoreOptions
                {
                    RootDirectory = temp.Path,
                    DefaultCollection = "items"
                });

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredKeyedService<IStorageStore>("items-store");

        var saved = await store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        saved.Collection.ShouldBe("items");
        Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Service_registration_rejects_null_options()
    {
        var services = new ServiceCollection();

        var storeException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowFileSystemStorageStore(
                "items-store",
                (FileSystemStorageStoreOptions)null!));
        var factoryException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowFileSystemStorageStoreFactory(
                "items-factory",
                (FileSystemStorageStoreOptions)null!));

        storeException.ParamName.ShouldBe("options");
        factoryException.ParamName.ShouldBe("options");
    }

    [Fact]
    public async Task Service_registration_rejects_null_options_factory_result()
    {
        var services = new ServiceCollection()
            .AddFluxFlowFileSystemStorageStore(
                "items-store",
                static _ => null!)
            .AddFluxFlowFileSystemStorageStoreFactory(
                "items-factory",
                static _ => null!);

        await using var provider = services.BuildServiceProvider();

        var storeException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IStorageStore>("items-store"));
        var factoryException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IStorageStoreFactory>("items-factory"));

        storeException.Message.ShouldBe("File-system storage options factory returned null.");
        factoryException.Message.ShouldBe("File-system storage options factory returned null.");
    }

    [Fact]
    public async Task Factory_DoesNotShareContextDefaultCollections()
    {
        using var temp = TempDirectory.Create();
        var factory = new FileSystemStorageStoreFactory(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path
        });

        await using var first = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "tenant-a",
            Collection = "items"
        });
        await using var second = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "tenant-a",
            Collection = "orders"
        });
        await first.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });
        await second.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "second"
        });

        var firstLoaded = await first.Store.GetAsync(new StorageGetRequest
        {
            Key = "alpha"
        });
        var secondLoaded = await second.Store.GetAsync(new StorageGetRequest
        {
            Key = "alpha"
        });

        first.Store.ShouldNotBeSameAs(second.Store);
        firstLoaded.ShouldNotBeNull();
        firstLoaded.Collection.ShouldBe("items");
        firstLoaded.Value.ShouldBe("first");
        secondLoaded.ShouldNotBeNull();
        secondLoaded.Collection.ShouldBe("orders");
        secondLoaded.Value.ShouldBe("second");
    }

    [Fact]
    public async Task Factory_SharesSameContextDefaultCollection()
    {
        using var temp = TempDirectory.Create();
        var factory = new FileSystemStorageStoreFactory(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path
        });

        await using var first = await factory.OpenAsync(CreateStoreContext());
        await using var second = await factory.OpenAsync(CreateStoreContext());
        await first.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        var loaded = await second.Store.GetAsync(new StorageGetRequest
        {
            Key = "alpha"
        });

        first.Store.ShouldBeSameAs(second.Store);
        loaded.ShouldNotBeNull();
        loaded.Collection.ShouldBe("items");
        loaded.Value.ShouldBe("first");
    }

    [Fact]
    public async Task Factory_SerializesVersionedPutsAcrossLeases()
    {
        using var temp = TempDirectory.Create();
        var factory = new FileSystemStorageStoreFactory(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path
        });
        await using var first = await factory.OpenAsync(CreateStoreContext());
        await using var second = await factory.OpenAsync(CreateStoreContext());
        var successes = 0;

        await Task.WhenAll(Enumerable.Range(0, 100).Select(async index =>
        {
            var store = index % 2 == 0 ? first.Store : second.Store;
            while (true)
            {
                var current = await store.GetAsync(new StorageGetRequest
                {
                    Collection = "items",
                    Key = "counter"
                });
                try
                {
                    await store.PutAsync(new StoragePutRequest
                    {
                        Collection = "items",
                        Key = "counter",
                        Value = index,
                        ExpectedVersion = current?.Version ?? 0
                    });
                    Interlocked.Increment(ref successes);
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }));
        var final = await first.Store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "counter"
        });

        successes.ShouldBe(100);
        final.ShouldNotBeNull();
        final.Version.ShouldBe(100);
    }

    [Fact]
    public async Task Factory_SerializesCreateModeAcrossLeases()
    {
        using var temp = TempDirectory.Create();
        var factory = new FileSystemStorageStoreFactory(new FileSystemStorageStoreOptions
        {
            RootDirectory = temp.Path
        });
        await using var first = await factory.OpenAsync(CreateStoreContext());
        await using var second = await factory.OpenAsync(CreateStoreContext());
        var successes = 0;
        var conflicts = 0;

        await Task.WhenAll(Enumerable.Range(0, 100).Select(async index =>
        {
            var store = index % 2 == 0 ? first.Store : second.Store;
            try
            {
                await store.PutAsync(new StoragePutRequest
                {
                    Collection = "items",
                    Key = "singleton",
                    Value = index,
                    Mode = StorageWriteMode.Create
                });
                Interlocked.Increment(ref successes);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref conflicts);
            }
        }));
        var final = await first.Store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "singleton"
        });

        successes.ShouldBe(1);
        conflicts.ShouldBe(99);
        final.ShouldNotBeNull();
        final.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Put_CreateSucceedsOverExpiredRecord()
    {
        var now = new DateTimeOffset(2026, 2, 3, 5, 5, 6, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path, clock);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "old",
            ExpiresAt = now.AddMinutes(-1)
        });

        var created = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "new",
            Mode = StorageWriteMode.Create
        });

        created.Version.ShouldBe(1);
        created.Value.ShouldBe("new");
    }

    [Fact]
    public async Task Query_SkipsCorruptRecordFilesAndRemovesTempFiles()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(temp.Path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });
        var collectionDirectory = Path.GetDirectoryName(
            Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories)
                .ShouldHaveSingleItem())!;
        var garbagePath = Path.Combine(collectionDirectory, "garbage.json");
        var unsupportedPath = Path.Combine(collectionDirectory, "unsupported.json");
        var tempPath = Path.Combine(collectionDirectory, $"orphan.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(garbagePath, "not-json");
        await File.WriteAllTextAsync(
            unsupportedPath,
            """{"formatVersion":99,"collection":"items","key":"beta"}""");
        await File.WriteAllTextAsync(tempPath, "leftover");

        var records = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items"
        });

        records.ShouldHaveSingleItem().Key.ShouldBe("alpha");
        File.Exists(tempPath).ShouldBeFalse();
    }

    [Fact]
    public void Options_ValidateRootDirectory()
    {
        Should.Throw<InvalidOperationException>(
            () => new FileSystemStorageStore(new FileSystemStorageStoreOptions()));

        using var temp = TempDirectory.Create();
        var missing = Path.Combine(temp.Path, "missing");
        Should.Throw<DirectoryNotFoundException>(
            () => new FileSystemStorageStore(new FileSystemStorageStoreOptions
            {
                RootDirectory = missing,
                CreateDirectory = false
            }));
    }

    [Fact]
    public void Options_NormalizeTextFieldsAndRejectInvalidValueLimit()
    {
        var options = new FileSystemStorageStoreOptions
        {
            RootDirectory = " data/storage ",
            StoreName = " tenant-a ",
            DefaultCollection = " items "
        };

        options.RootDirectory.ShouldBe("data/storage");
        options.StoreName.ShouldBe("tenant-a");
        options.DefaultCollection.ShouldBe("items");

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FileSystemStorageStoreOptions
            {
                MaxValueBytes = 0
            });
    }

    private static FileSystemStorageStore CreateStore(
        string rootDirectory,
        TimeProvider? clock = null)
        => new(new FileSystemStorageStoreOptions
        {
            RootDirectory = rootDirectory,
            Clock = clock
        });

    private static StorageStoreContext CreateStoreContext()
        => new()
        {
            StoreName = "tenant-a",
            Collection = "items"
        };

    private sealed class AdvancingTimeProvider(
        DateTimeOffset start,
        TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow()
        {
            var current = _current;
            _current = _current.Add(step);
            return current;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "fluxflow-storage-filesystem-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
