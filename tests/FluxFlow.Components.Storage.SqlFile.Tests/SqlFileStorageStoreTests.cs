using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.SqlFile.Tests;

public sealed class SqlFileStorageStoreTests
{
    [Fact]
    public async Task Put_PersistsRecordAcrossStoreInstances()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        await using var store = CreateStore(path);

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

        await using var reopened = CreateStore(path);
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
        var now = new DateTimeOffset(2026, 2, 3, 6, 1, 2, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"), clock);

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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));

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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));

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
        var now = new DateTimeOffset(2026, 2, 3, 6, 2, 3, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"), clock);
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
    public async Task Put_CreateSucceedsOverExpiredRecord()
    {
        var now = new DateTimeOffset(2026, 2, 3, 6, 5, 6, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"), clock);
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
    public async Task Put_ReturnsTimestampsMatchingPersistedRecord()
    {
        var now = new DateTimeOffset(2026, 2, 3, 6, 6, 7, 123, TimeSpan.Zero).AddTicks(4567);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"), clock);

        var saved = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first",
            ExpiresAt = now.AddMinutes(5).AddTicks(789)
        });
        var loaded = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        loaded.ShouldNotBeNull();
        saved.StoredAt.ShouldBe(loaded.StoredAt);
        saved.ExpiresAt.ShouldBe(loaded.ExpiresAt);
        saved.Version.ShouldBe(loaded.Version);
    }

    [Fact]
    public async Task Query_FiltersRecordsAndHonorsLimit()
    {
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));

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
        var now = new DateTimeOffset(2026, 2, 3, 6, 3, 4, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"), clock);
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
        var now = new DateTimeOffset(2026, 2, 3, 6, 4, 5, TimeSpan.Zero);
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        await using (var writer = CreateStore(path, new FakeTimeProvider(now)))
        {
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
        }

        await using var reader = CreateStore(
            path,
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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
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
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));

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
    public async Task Query_PagesWithoutDuplicatesOrGaps()
    {
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        foreach (var index in Enumerable.Range(0, 9))
        {
            await store.PutAsync(new StoragePutRequest
            {
                Collection = "items",
                Key = $"page-{index}",
                Value = index
            });
        }

        var keys = new List<string>();
        foreach (var offset in new[] { 0, 3, 6 })
        {
            var page = await store.QueryAsync(new StorageQueryRequest
            {
                Collection = "items",
                KeyPrefix = "page-",
                Offset = offset,
                Limit = 3
            });
            page.Count.ShouldBe(3);
            keys.AddRange(page.Select(record => record.Key));
        }

        keys.ShouldBe(Enumerable.Range(0, 9).Select(index => $"page-{index}"));
    }

    [Fact]
    public async Task Query_EscapesKeyPrefixWildcards()
    {
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a%b-1",
            Value = "percent"
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "axb-1",
            Value = "other"
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a_b-1",
            Value = "underscore"
        });

        var percent = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            KeyPrefix = "a%b"
        });
        var underscore = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            KeyPrefix = "a_b"
        });

        percent.ShouldHaveSingleItem().Key.ShouldBe("a%b-1");
        underscore.ShouldHaveSingleItem().Key.ShouldBe("a_b-1");
    }

    [Fact]
    public async Task DisposeAsync_ReleasesDatabaseFile()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        var store = CreateStore(path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });

        await store.DisposeAsync();
        File.Delete(path);

        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_ReturnsFoundAndMissingResults()
    {
        using var temp = TempDirectory.Create();
        await using var store = CreateStore(Path.Combine(temp.Path, "records.db"));
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
    public async Task Put_RejectsValuesAboveConfiguredLimit()
    {
        using var temp = TempDirectory.Create();
        await using var store = new SqlFileStorageStore(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db"),
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
    public async Task Factory_UsesContextDefaultsAndCreatesOwnedLease()
    {
        using var temp = TempDirectory.Create();
        var factory = new SqlFileStorageStoreFactory(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db"),
            DefaultCollection = "fallback"
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

        lease.OwnsStore.ShouldBeTrue();
        saved.Collection.ShouldBe("items");
    }

    [Fact]
    public async Task Service_registration_can_register_keyed_store_directly()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorageStore(
                "items-store",
                new SqlFileStorageStoreOptions
                {
                    DatabasePath = path,
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
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task Service_registration_can_register_keyed_store_factory()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorageStoreFactory(
                "items-factory",
                new SqlFileStorageStoreOptions
                {
                    DatabasePath = path,
                    DefaultCollection = "fallback"
                });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<IStorageStoreFactory>("items-factory");
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

        lease.OwnsStore.ShouldBeTrue();
        saved.Collection.ShouldBe("items");
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var options = new SqlFileStorageStoreOptions
        {
            DatabasePath = "data/storage.db"
        };

        Should.Throw<ArgumentNullException>(() =>
            SqlFileStorageServiceCollectionExtensions.AddFluxFlowSqlFileStorageStore(
                null!,
                "items-store",
                options))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSqlFileStorageStore(" ", options))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileStorageStore(
                "items-store",
                (SqlFileStorageStoreOptions)null!))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileStorageStore(
                "items-store",
                (Func<IServiceProvider, SqlFileStorageStoreOptions>)null!))
            .ParamName.ShouldBe("optionsFactory");

        Should.Throw<ArgumentNullException>(() =>
            SqlFileStorageServiceCollectionExtensions.AddFluxFlowSqlFileStorageStoreFactory(
                null!,
                "items-factory",
                options))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSqlFileStorageStoreFactory(" ", options))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileStorageStoreFactory(
                "items-factory",
                (SqlFileStorageStoreOptions)null!))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileStorageStoreFactory(
                "items-factory",
                (Func<IServiceProvider, SqlFileStorageStoreOptions>)null!))
            .ParamName.ShouldBe("optionsFactory");
    }

    [Fact]
    public async Task Service_registration_rejects_null_options_factory_result()
    {
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorageStore(
                "items-store",
                static _ => null!)
            .AddFluxFlowSqlFileStorageStoreFactory(
                "items-factory",
                static _ => null!);

        await using var provider = services.BuildServiceProvider();

        var storeException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IStorageStore>("items-store"));
        var factoryException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IStorageStoreFactory>("items-factory"));

        storeException.Message.ShouldBe("SQL-file storage options factory returned null.");
        factoryException.Message.ShouldBe("SQL-file storage options factory returned null.");
    }

    [Fact]
    public async Task Factory_SerializesVersionedPutsAcrossLeases()
    {
        using var temp = TempDirectory.Create();
        var factory = new SqlFileStorageStoreFactory(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db")
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
        var factory = new SqlFileStorageStoreFactory(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db")
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
    public void Options_NormalizeTextFieldsAndRejectInvalidNumericLimits()
    {
        var options = new SqlFileStorageStoreOptions
        {
            DatabasePath = " data/storage.db ",
            StoreName = " tenant-a ",
            DefaultCollection = " items "
        };

        options.DatabasePath.ShouldBe("data/storage.db");
        options.StoreName.ShouldBe("tenant-a");
        options.DefaultCollection.ShouldBe("items");

        Should.Throw<ArgumentOutOfRangeException>(() => new SqlFileStorageStoreOptions
        {
            MaxValueBytes = 0
        });
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlFileStorageStoreOptions
        {
            BusyTimeoutMilliseconds = 0
        });
    }

    [Fact]
    public void Options_RejectAbsoluteDatabasePathWhenDisabled()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");

        var exception = Should.Throw<InvalidOperationException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions
            {
                DatabasePath = path,
                AllowAbsoluteDatabasePath = false
            }));

        exception.Message.ShouldBe(
            "SQL file storage database path cannot be absolute when absolute paths are disabled.");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Options_ValidateDatabasePath()
    {
        Should.Throw<InvalidOperationException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions()));

        using var temp = TempDirectory.Create();
        var missing = Path.Combine(temp.Path, "missing", "records.db");
        Should.Throw<DirectoryNotFoundException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions
            {
                DatabasePath = missing,
                CreateDirectory = false
            }));

        Should.Throw<FileNotFoundException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions
            {
                DatabasePath = Path.Combine(temp.Path, "missing.db"),
                CreateDatabase = false
            }));
    }

    private static SqlFileStorageStore CreateStore(
        string databasePath,
        TimeProvider? clock = null)
        => new(new SqlFileStorageStoreOptions
        {
            DatabasePath = databasePath,
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

}

internal sealed class TempDirectory : IDisposable
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
            $"fluxflow-sqlfile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 5)
            {
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
            }

            Thread.Sleep(100);
        }
    }
}
