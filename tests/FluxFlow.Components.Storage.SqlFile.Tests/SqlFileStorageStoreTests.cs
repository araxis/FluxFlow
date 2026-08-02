using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.SqlFile.Tests;

public sealed class SqlFileStorageStoreTests
{
    [Fact]
    public async Task Canonical_nodes_round_trip_exact_content_across_store_instances()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "content.db");
        byte[] bytes = [0x00, 0x7F, 0xFF];
        await using (var store = CreateStore(path))
        await using (var put = new StoragePutNode(
                         store,
                         new StoragePutOptions { Collection = "items" }))
        {
            var output = Link(put.Output);
            await put.Input.SendAsync(FlowMessage.Create(new StorageContentPutRequest
            {
                Key = "content",
                Content = FlowContent.FromBytes(bytes, "application/octet-stream", "binary")
            }));
            (await output.ReceiveAsync()).IsError.ShouldBeFalse();
        }

        await using var reopened = CreateStore(path);
        await using var get = new StorageGetNode(
            reopened,
            new StorageGetOptions { Collection = "items" });
        var results = Link(get.Output);
        await get.Input.SendAsync(FlowMessage.Create(new StorageGetRequest { Key = "content" }));

        var record = (await results.ReceiveAsync()).Value.Record.ShouldNotBeNull();
        record.Content.Bytes.AsSpan().ToArray().ShouldBe(bytes);
        record.Content.ContentType.ShouldBe("application/octet-stream");
        record.Content.Encoding.ShouldBe("binary");
    }

    [Fact]
    public async Task Put_PersistsRecordAcrossStoreInstances()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        await using var store = CreateStore(path);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "test"
        };
        var request = new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first",
            ContentType = "text/plain",
            Attributes = attributes,
            CorrelationId = "c-1"
        };
        attributes["source"] = "changed";
        attributes["later"] = "ignored";

        var saved = await store.PutAsync(request);

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
        loaded.Attributes.ContainsKey("Source").ShouldBeFalse();
        loaded.Attributes.ContainsKey("later").ShouldBeFalse();
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
    public async Task Flat_registration_configures_one_trimmed_keyed_factory()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "configured", "records.db");
        var now = DateTimeOffset.Parse("2026-07-28T09:00:00Z");
        var clock = new FakeTimeProvider(now);
        var callbackCount = 0;
        var services = new ServiceCollection();

        var returned = services.AddFluxFlowSqlFileStorage(" records ", registration =>
        {
            callbackCount++;
            registration.DatabasePath = path;
            registration.CreateDatabase = true;
            registration.CreateDirectory = true;
            registration.AllowAbsoluteDatabasePath = true;
            registration.MaxValueBytes = 7;
            registration.DefaultCollection = "items";
            registration.BusyTimeoutMilliseconds = 1_000;
            registration.Clock = clock;
        });

        returned.ShouldBeSameAs(services);
        callbackCount.ShouldBe(1);
        Directory.Exists(Path.GetDirectoryName(path)).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(SqlFileStorageRegistrationBuilder) ||
            descriptor.ServiceType == typeof(Action<SqlFileStorageRegistrationBuilder>))
            .ShouldBeFalse();
        await using var provider = services.BuildServiceProvider();
        provider.GetKeyedService<IStorageStoreFactory>(" records ").ShouldBeNull();
        provider.GetKeyedService<IStorageStore>("records").ShouldBeNull();
        var factory = provider.GetRequiredKeyedService<IStorageStoreFactory>("records");
        await using var lease = await factory.OpenAsync(new StorageStoreContext());

        var saved = await lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        lease.OwnsStore.ShouldBeTrue();
        saved.Collection.ShouldBe("items");
        saved.StoredAt.ShouldBe(now);
        File.Exists(path).ShouldBeTrue();
        await using var explicitNameLease = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "records"
        });
        await using var defaultNameLease = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "default"
        });
        (await explicitNameLease.Store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        })).ShouldNotBeNull();
        (await defaultNameLease.Store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        })).ShouldBeNull();
        await Should.ThrowAsync<InvalidOperationException>(() => lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "large",
            Value = "abcdef"
        }));
    }

    [Fact]
    public async Task Flat_registration_preserves_owned_factory_leases()
    {
        using var temp = TempDirectory.Create();
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorage("items", registration =>
                registration.DatabasePath = Path.Combine(temp.Path, "records.db"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<IStorageStoreFactory>("items");
        var context = new StorageStoreContext
        {
            StoreName = "tenant-a",
            Collection = "records"
        };
        await using var first = await factory.OpenAsync(context);
        await using var second = await factory.OpenAsync(context);

        first.OwnsStore.ShouldBeTrue();
        second.OwnsStore.ShouldBeTrue();
        first.Store.ShouldNotBeSameAs(second.Store);
    }

    [Fact]
    public void Flat_registration_rejects_invalid_arguments_and_builder_values()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            SqlFileStorageServiceCollectionExtensions.AddFluxFlowSqlFileStorage(
                null!,
                "items",
                static _ => { }))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowSqlFileStorage(" ", static _ => { }))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileStorage(
                "items",
                (Action<SqlFileStorageRegistrationBuilder>)null!))
            .ParamName.ShouldBe("configure");

        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowSqlFileStorage("missing", static _ => { }))
            .Message.ShouldBe("SQL file storage registration requires a database path.");
        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowSqlFileStorage("invalid-limit", static registration =>
            {
                registration.DatabasePath = "data/storage.db";
                registration.MaxValueBytes = 0;
            }))
            .Message.ShouldBe("SQL file storage max value bytes must be greater than zero.");
        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowSqlFileStorage("invalid-timeout", static registration =>
            {
                registration.DatabasePath = "data/storage.db";
                registration.BusyTimeoutMilliseconds = 0;
            }))
            .Message.ShouldBe("SQL file storage busy timeout must be greater than zero.");
    }

    [Fact]
    public async Task Flat_registration_projects_creation_and_absolute_path_policies()
    {
        using var temp = TempDirectory.Create();
        var missingDirectoryPath = Path.Combine(temp.Path, "missing", "records.db");
        var missingDatabasePath = Path.Combine(temp.Path, "missing.db");
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorage("missing-directory", registration =>
            {
                registration.DatabasePath = missingDirectoryPath;
                registration.CreateDirectory = false;
            })
            .AddFluxFlowSqlFileStorage("missing-database", registration =>
            {
                registration.DatabasePath = missingDatabasePath;
                registration.CreateDatabase = false;
            })
            .AddFluxFlowSqlFileStorage("relative-only", registration =>
            {
                registration.DatabasePath = Path.Combine(temp.Path, "relative.db");
                registration.AllowAbsoluteDatabasePath = false;
            });
        await using var provider = services.BuildServiceProvider();
        var context = new StorageStoreContext { StoreName = "tenant-a" };

        var missingDirectoryFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("missing-directory");
        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
            await missingDirectoryFactory.OpenAsync(context));
        var missingDatabaseFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("missing-database");
        await Should.ThrowAsync<FileNotFoundException>(async () =>
            await missingDatabaseFactory.OpenAsync(context));
        var relativeOnlyFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("relative-only");
        var absoluteError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await relativeOnlyFactory.OpenAsync(context));
        absoluteError.Message.ShouldContain("absolute paths are disabled");
    }

    [Fact]
    public void Flat_registration_rejects_duplicate_key_before_second_callback()
    {
        var firstCallbackCount = 0;
        var secondCallbackCount = 0;
        var services = new ServiceCollection()
            .AddFluxFlowSqlFileStorage(" records ", registration =>
            {
                firstCallbackCount++;
                registration.DatabasePath = "data/records.db";
            });

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileStorage("records", registration =>
            {
                secondCallbackCount++;
                registration.DatabasePath = "other/records.db";
            }));

        exception.Message.ShouldBe(
            "SQL file storage store factory 'records' is already registered.");
        firstCallbackCount.ShouldBe(1);
        secondCallbackCount.ShouldBe(0);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IStorageStoreFactory) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, "records")).ShouldBe(1);
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

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer);
        return buffer;
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
