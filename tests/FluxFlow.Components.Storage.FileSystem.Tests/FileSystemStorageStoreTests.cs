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

namespace FluxFlow.Components.Storage.FileSystem.Tests;

public sealed class FileSystemStorageStoreTests
{
    [Fact]
    public async Task Canonical_nodes_round_trip_exact_content_across_store_instances()
    {
        using var temp = TempDirectory.Create();
        byte[] bytes = [0x00, 0x7F, 0xFF];
        await using (var put = new StoragePutNode(
                         CreateStore(temp.Path),
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

        await using var get = new StorageGetNode(
            CreateStore(temp.Path),
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
        var store = CreateStore(temp.Path);
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
        loaded.Attributes.ContainsKey("Source").ShouldBeFalse();
        loaded.Attributes.ContainsKey("later").ShouldBeFalse();
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
    public async Task Flat_registration_configures_one_trimmed_keyed_factory()
    {
        using var temp = TempDirectory.Create();
        var root = Path.Combine(temp.Path, "configured");
        var now = DateTimeOffset.Parse("2026-07-28T08:00:00Z");
        var clock = new FakeTimeProvider(now);
        var callbackCount = 0;
        var services = new ServiceCollection();

        var returned = services.AddFluxFlowFileSystemStorage(" items ", registration =>
        {
            callbackCount++;
            registration.RootDirectory = root;
            registration.CreateDirectory = true;
            registration.AllowAbsoluteRootDirectory = true;
            registration.MaxValueBytes = 7;
            registration.DefaultCollection = "records";
            registration.FlushOnWrite = false;
            registration.Clock = clock;
        });

        returned.ShouldBeSameAs(services);
        callbackCount.ShouldBe(1);
        Directory.Exists(root).ShouldBeFalse();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(FileSystemStorageRegistrationBuilder) ||
            descriptor.ServiceType == typeof(Action<FileSystemStorageRegistrationBuilder>))
            .ShouldBeFalse();
        await using var provider = services.BuildServiceProvider();
        provider.GetKeyedService<IStorageStoreFactory>(" items ").ShouldBeNull();
        provider.GetKeyedService<IStorageStore>("items").ShouldBeNull();
        var factory = provider.GetRequiredKeyedService<IStorageStoreFactory>("items");
        await using var lease = await factory.OpenAsync(new StorageStoreContext());
        await using var explicitNameLease = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "items"
        });
        await using var defaultNameLease = await factory.OpenAsync(new StorageStoreContext
        {
            StoreName = "default"
        });

        var saved = await lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        lease.OwnsStore.ShouldBeFalse();
        explicitNameLease.Store.ShouldBeSameAs(lease.Store);
        defaultNameLease.Store.ShouldNotBeSameAs(lease.Store);
        saved.Collection.ShouldBe("records");
        saved.StoredAt.ShouldBe(now);
        Directory.Exists(root).ShouldBeTrue();
        Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ShouldHaveSingleItem();
        await Should.ThrowAsync<InvalidOperationException>(() => lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "large",
            Value = "abcdef"
        }));
    }

    [Fact]
    public async Task Flat_registration_preserves_shared_factory_leases()
    {
        using var temp = TempDirectory.Create();
        var services = new ServiceCollection()
            .AddFluxFlowFileSystemStorage("items", registration =>
                registration.RootDirectory = temp.Path);
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<IStorageStoreFactory>("items");
        var context = new StorageStoreContext
        {
            StoreName = "tenant-a",
            Collection = "records"
        };
        await using var first = await factory.OpenAsync(context);
        await using var second = await factory.OpenAsync(context);

        first.OwnsStore.ShouldBeFalse();
        second.OwnsStore.ShouldBeFalse();
        first.Store.ShouldBeSameAs(second.Store);
        await first.DisposeAsync();

        var saved = await second.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });
        saved.Collection.ShouldBe("records");
    }

    [Fact]
    public void Flat_registration_rejects_invalid_arguments_and_builder_values()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            FileSystemStorageServiceCollectionExtensions.AddFluxFlowFileSystemStorage(
                null!,
                "items",
                static _ => { }))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowFileSystemStorage(" ", static _ => { }))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowFileSystemStorage(
                "items",
                (Action<FileSystemStorageRegistrationBuilder>)null!))
            .ParamName.ShouldBe("configure");

        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowFileSystemStorage("missing", static _ => { }))
            .Message.ShouldBe("File-system storage registration requires a root directory.");
        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowFileSystemStorage("invalid", static registration =>
            {
                registration.RootDirectory = "data/storage";
                registration.MaxValueBytes = 0;
            }))
            .Message.ShouldBe("File-system storage max value bytes must be greater than zero.");
    }

    [Fact]
    public async Task Flat_registration_projects_directory_and_absolute_path_policies()
    {
        using var temp = TempDirectory.Create();
        var missingRoot = Path.Combine(temp.Path, "missing");
        var services = new ServiceCollection()
            .AddFluxFlowFileSystemStorage("missing", registration =>
            {
                registration.RootDirectory = missingRoot;
                registration.CreateDirectory = false;
            })
            .AddFluxFlowFileSystemStorage("relative-only", registration =>
            {
                registration.RootDirectory = temp.Path;
                registration.AllowAbsoluteRootDirectory = false;
            });
        await using var provider = services.BuildServiceProvider();

        var missingFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("missing");
        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
            await missingFactory.OpenAsync(new StorageStoreContext { StoreName = "tenant-a" }));
        var relativeOnlyFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("relative-only");
        var absoluteError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await relativeOnlyFactory.OpenAsync(new StorageStoreContext { StoreName = "tenant-a" }));
        absoluteError.Message.ShouldContain("absolute roots are disabled");
    }

    [Fact]
    public void Flat_registration_rejects_duplicate_key_before_second_callback()
    {
        var firstCallbackCount = 0;
        var secondCallbackCount = 0;
        var services = new ServiceCollection()
            .AddFluxFlowFileSystemStorage(" items ", registration =>
            {
                firstCallbackCount++;
                registration.RootDirectory = "data/storage";
            });

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowFileSystemStorage("items", registration =>
            {
                secondCallbackCount++;
                registration.RootDirectory = "other/storage";
            }));

        exception.Message.ShouldBe(
            "File-system storage store factory 'items' is already registered.");
        firstCallbackCount.ShouldBe(1);
        secondCallbackCount.ShouldBe(0);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IStorageStoreFactory) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, "items")).ShouldBe(1);
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

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer);
        return buffer;
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
